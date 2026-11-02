# Nghiệm thu B2 — Quyền xem giá trong thư viện hóa đơn đầu vào

**Đã triển khai phương án A trong source và kiểm thử đạt. Chưa publish hoặc triển khai lên ứng dụng đang chạy.**

Bản build chốt: **0 lỗi, 0 cảnh báo**. Kiểm thử liên quan: **191/191 ca đạt**, gồm **84 ca mới cho B2** và **24 ca hồi quy B1**; không có ca bỏ qua. Lượt cuối chạy khoảng 4 phút 39 giây trên cùng bản build và bộ source được ghi hash trước khi chạy. Đây là nghiệm thu B2 và các phần liên quan, không phải chạy lại toàn bộ hệ thống.

## Quy tắc quyền đã áp dụng

Vẫn phải có `inventory.stockdocument.view` để vào thư viện. Quyền thương mại được lấy từ authorization phía server; không đọc cờ quyền do query string hoặc JSON gửi lên.

| Quyền bổ sung | Hóa đơn được xem giá, xem trước và tải bản gốc | Được lưu xác nhận nội bộ |
| --- | --- | --- |
| Không có, hoặc chỉ có quyền nhập/cập nhật XML | Không có; vẫn xem metadata | Không |
| `purchase.order.viewcost` | Các hóa đơn trong phạm vi cửa hàng/người mua hợp lệ | Không, nếu không có quyền duyệt phù hợp |
| `inventory.stockdocument.approve` | Chưa liên kết hoặc chỉ liên kết phiếu nhập trực tiếp | Những hóa đơn tương ứng |
| `purchase.receipt.approve` | Chỉ liên kết phiếu nhập theo đơn mua | Những hóa đơn tương ứng |
| Cả hai quyền duyệt trên | Các hóa đơn thuộc hai luồng, gồm hóa đơn dùng chung | Những hóa đơn tương ứng |

Phiếu có `PurchaseOrderId` cũng được nhận diện là luồng đơn mua, kể cả khi cờ nguồn còn mang giá trị nhập trực tiếp. Hóa đơn liên kết cả hai luồng cần đủ hai quyền duyệt, hoặc quyền xem giá để đọc. Quyền xem giá không tự cấp quyền lưu xác nhận. Cửa hàng khác tiếp tục nhận 404 với ID hóa đơn không thuộc phạm vi.

## Thay đổi thực tế

- Danh sách và chi tiết trả `canViewCost`; chi tiết trả thêm `canReview` theo từng hóa đơn. Người thiếu quyền nhận `null` ở các trường tiền, không nhận giá trị thật rồi chỉ bị ẩn bằng giao diện. Số hóa đơn, ngày, nhà cung cấp, người mua, trạng thái và liên kết phiếu vẫn được phép xem.
- Tổng tiền một nhóm chỉ được trả khi người đọc xem được giá của toàn bộ hóa đơn trong nhóm đó. Truy vấn tổng chỉ cộng các hóa đơn được phép; không trả tổng chứa giá bị hạn chế hoặc một tổng bộ phận dễ gây hiểu nhầm. Số lượng bản ghi vẫn là metadata.
- Ghi chú xác nhận, ghi chú lịch sử và tên tự đặt của phiếu liên kết được che khi thiếu quyền xem giá vì các trường tự do này có thể chứa giá. Lịch sử trạng thái/thời điểm/người thao tác vẫn được giữ.
- Preview, PDF và XML cùng kiểm tra quyền ở service trước khi đọc tệp. Thiếu quyền nhận HTTP 403; khác phạm vi nhận 404. Giữ `Cache-Control: no-store`, kiểm tra hash XML và byte gốc khi được phép tải. Không sửa XML đã ký hoặc tạo bản gốc giả đã che giá.
- Giao diện hiển thị “Không có quyền xem giá”, ẩn nút xem/tải, không gửi yêu cầu preview khi thiếu quyền. Quyền lưu xác nhận được cập nhật theo từng hóa đơn. Link tải và preview cũ được dọn khi chuyển/đóng hộp thoại. Tiền bằng 0 thật vẫn hiển thị 0 đ khi có quyền.
- Hộp thoại chỉ xem metadata được thu gọn thành một cột; đã kiểm tra ảnh desktop và điện thoại 390×740. Thông tin hóa đơn xuất hiện ngay dưới thông báo giới hạn quyền.

Các API XML trong phiếu nhập tiếp tục kiểm tra quyền duyệt theo ngữ cảnh từng phiếu. Các ca HTTP bổ sung xác nhận tài khoản chỉ xem không lấy được dữ liệu qua danh sách XML, line-maps, reconciliation, picker và preview tệp liên kết. URL upload công khai cũng không phục vụ XML/PDF gốc.

## Kiểm thử và phạm vi thay đổi

84 ca B2 mới gồm 30 ca đọc JSON/preview/tệp theo ma trận quyền, 30 ca lưu xác nhận, 6 ca tổng hợp nhiều loại quyền, 4 ca khác cửa hàng và các ca số tiền 0, giả cờ quyền, kiểm tra lại sau khi liên kết thay đổi, quyền xem cơ sở, CSRF, 8 đường XML từ phiếu nhập, 2 URL công khai và Chrome thật. Chrome kiểm tra bốn nhóm quyền, chuyển qua hóa đơn có quyền khác nhau, tải/xem nguyên bản khi được phép, lưu xác nhận hợp lệ và bố cục di động.

107 ca hồi quy còn lại gồm thư viện/XML, cô lập tenant, picker, luồng quyền phiếu nhập, phân bổ giá, an toàn phiếu nhập và 24 ca B1. Các log attempt được giữ lại: một lần test Chrome chưa chờ sự kiện đóng dialog; một ca khác dùng nhầm GET cho endpoint chỉ nhận POST. Đã sửa mã kiểm thử và chạy lại toàn bộ 191 ca trên bản chốt, đạt hết.

Đối chiếu hash **3.728 tệp** xác nhận chỉ năm tệp ứng dụng của B2 thay đổi trong tập đã nghiệm thu:

- `GaoApp.Infrastructure/Services/Invoices/InputInvoiceCatalogService.cs`
- `GaoApp.Web/Areas/Admin/Controllers/InputInvoiceLibraryController.cs`
- `GaoApp.Web/Areas/Admin/Views/InputInvoiceLibrary/Index.cshtml`
- `GaoApp.Web/wwwroot/Admin/js/input-invoice-library.js`
- `GaoApp.Web/wwwroot/Admin/css/input-invoice-library.css`

Bổ sung `InputInvoiceCommercialAccessSqlServerTests.cs` và `input-invoice-commercial-access.browser.cjs`. Các tệp source/test của B1 giữ nguyên hash. Không thêm migration cho B2, không thay quyền/menu vận hành, không ghi GaoAppDb. Kiểm thử dùng CSDL GUID và tài khoản tổng hợp; không gọi ngân hàng, hóa đơn điện tử hoặc máy in thật.

Sau kiểm thử, chỉ còn đúng **7 CSDL GaoApp có từ trước**, đều ONLINE; không còn CSDL test mới. Khi phát hành cần đưa Web/Infrastructure và JS/CSS tương ứng vào cùng bản ứng dụng. B3–B4 tiếp tục là các mục riêng; chưa triển khai trong lượt B2.

## Bằng chứng

Thư mục `.artifacts/b2-invoice-cost-access-20261023/`:

- `build-verified.log`, `results/b2-regression.trx`, `regression.log`: kết quả bản chốt.
- `run-regression.ps1`, `regression-filter.txt`: lệnh và phạm vi chạy lại.
- `source-before-run.json`, `verification.json`: hash source/binary và kiểm tra source không đổi trong lúc chạy.
- `before/`, `before-hashes.json`, các tệp `.diff`: baseline riêng B2 và phạm vi chỉnh sửa, giữ các thay đổi đã có của workspace.
- `browser/chrome.log`, các ảnh `.png`: bốn nhóm quyền và bố cục desktop/mobile.
- `databases-after.txt`: bảy CSDL có từ trước được giữ nguyên, đều ONLINE.
