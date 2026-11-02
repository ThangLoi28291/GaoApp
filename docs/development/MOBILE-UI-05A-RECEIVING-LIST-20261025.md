# UI-05a — Danh sách phiếu nhập trên điện thoại

Ngày 25/10/2026. Đã triển khai và kiểm thử tự động UI-05a tại `/admin/warehouse-receiving`. Bước tiếp theo **UI-05b — chi tiết phiếu nhận hàng theo T04**. Nghiệm thu thiết bị Android/iOS thật còn riêng.

## Giao diện đã thay đổi

Chỉ dưới 768 px: thống kê Đang nhập / Chờ duyệt / Cần sửa thành ba ô gọn; giữ tìm kiếm và tải lại, thêm phần lọc kho/HKD và nguồn phiếu có thể mở riêng. Thẻ phiếu có mã, trạng thái, tên đầy đủ, ngày tạo, kho/HKD, số dòng/loại sản phẩm, nhà cung cấp và giai đoạn xử lý. Các nút Tiếp tục nhập / Xem phiếu / Sửa phiếu vẫn dùng đúng đường dẫn và điều kiện cũ.

Nút đổi tên/xóa vẫn do `_ReceiptDocumentActions` và quyền máy chủ quyết định; trên phone bố trí cạnh nhau, còn nút xử lý phiếu chiếm đủ chiều ngang. Form tạo phiếu giữ nguyên trường nhập, lựa chọn nguồn, HKD/kho mặc định và handler. Chỉ nâng cách hiển thị: cuộn phần thân ở màn hình thấp, input 16 px, nút đóng 44 px trong khung, màu lam theo mẫu duyệt. Chuyển kích thước khi form đang mở giữ tên/ghi chú/nguồn/kho đã chọn.

Đã xem ảnh và sửa độ ưu tiên CSS khiến biểu tượng/thống kê cũ chiếm nhiều chiều cao. Thẻ không cắt tên dài; các nút tác vụ đạt chiều cao tối thiểu 44 px. Chuyển động nhẹ được tắt theo reduced motion; có nhánh forced colors. Các thẻ dài tiếp tục cuộn trong trang theo shell hiện có.

## Dữ liệu và quyền giữ nguyên

- Không đổi giao diện từ 768 px, controller/service/repository/DTO, layout/menu/ghim/viewport chung, Chrome launcher hoặc migration.
- Giữ một bộ node/nút gốc, dùng anchor để chuyển mã/trạng thái/nút tạo trên phone và khôi phục khi về desktop. Base renderer chỉ thêm hook đọc trước lọc/sau render. Adapter không gọi API, storage, submit hoặc sao chép control nghiệp vụ.
- Tìm kiếm, trạng thái và phân trang vẫn do renderer cũ xử lý. Lọc mới chạy cục bộ trên đúng danh sách máy chủ đã trả; thống kê phía trên vẫn tính toàn bộ phiếu cần xử lý và có chú thích rõ.
- Nguồn là **Theo đơn mua / Không gắn đơn mua** dựa trên `PurchaseOrderId`. API hiện tại không trả lý do nhập trực tiếp; không dựng thêm các nguồn nghiệp vụ từ dữ liệu thiếu.
- Lọc kho dùng `LegalEntityId` cùng tên kho có sẵn trong summary. Hai kho trùng tên trong cùng HKD được nhóm chung vì API không có `WarehouseId`; không thay hợp đồng API để suy đoán định danh.
- Tiến trình là giai đoạn **Nhập hàng/Cần sửa → Chờ duyệt** theo trạng thái. Không hiển thị phần trăm hoàn tất hay cộng số lượng khác đơn vị khi summary chưa có dữ liệu đó.
- Menu tiếp tục do `AdminMenuItems` và quyền/cấu hình cửa hàng quyết định. Không thêm menu qua seeder, script, migration hoặc dữ liệu thử mới của bước này.

## Kết quả kiểm thử

- Build Release Web/Razor/Tests/runner: **0 lỗi, 0 cảnh báo**.
- **7/7 hồi quy**: 3 Razor/selector contract, 2 danh sách phiếu SQL, 2 CSP. Bộ SQL có 1.000 phiếu/20.000 dòng và kiểm tra projection, query count, số dòng, liên kết và phạm vi cửa hàng.
- **28 nhóm browser** trên Razor/HTTP/SQL thật; 10 kích thước: 1600, 1440, 1366, 1024, 768, 767, 430, 390, 360, 320 px.
- Fixture có 24 phiếu cần xử lý, ba trạng thái, hai kho/HKD, có/không đơn mua, phiếu trống, tên dài có ký tự HTML; cùng phiếu đã duyệt/hủy/xóa và cửa hàng khác phải bị loại khỏi kết quả.
- Qua lọc kết hợp kho/HKD/nguồn/trạng thái, tìm kiếm, phân trang, số thống kê, trạng thái rỗng, CTA và giai đoạn xử lý. Lọc/resize không phát sinh thêm request danh sách; lọc riêng của phone được bỏ khỏi kết quả desktop và phục hồi khi quay lại phone.
- Form tạo phiếu giữ đúng mặc định từ API, đổi HKD chọn đúng kho; dữ liệu đang nhập còn nguyên khi đổi giữa 320×568, 390×440, desktop và 390×844. Chờ viewport/Bootstrap ổn định trước khi đo khung; đây là mô phỏng chiều cao, không phải bàn phím thật.
- Qua lỗi tải lại 500/403, HTML trả 200, lỗi mạng và timeout: giữ danh sách cũ, thông báo và thử lại; lần tải đầu thất bại không hiện thành danh sách rỗng thành công. Nhấn tải lặp chỉ có một request đang chạy và trạng thái `aria-busy` đúng. Các lỗi này được chủ động tiêm vào GET để kiểm tra phục hồi.
- Nhân viên chỉ có quyền tạo nhập hàng không nhận nút đổi tên/xóa; tài khoản chỉ có quyền xem nhận JSON 403 và trang HTML chuyển tới access-denied theo hành vi máy chủ cũ.
- Không tràn ngang ở các viewport, forced colors và chữ 150%; không có lỗi JavaScript/CSP hay POST nghiệp vụ.

DOM/nội dung desktop khôi phục đúng, các node gốc còn kết nối. Chỉ chuẩn hóa thuộc tính rỗng `style=""` trên input `wrSearch` do jQuery đo kích thước để so HTML; không bỏ qua thuộc tính/text/thứ tự khác. Ảnh desktop 1440 px khớp hoàn toàn khi bật/tắt CSS phone, cố định animation trong phép chụp để loại chuyển động sidebar/chấm online sẵn có.

**60 file bảo vệ giữ hash**; view chỉ khác marker/import, renderer chỉ khác các hook. Baseline gốc 560 file được giữ nguyên: **521 không đổi, 39 đổi cộng dồn, 0 mất**. Snapshot SQL trước/sau browser giữ nguyên chứng từ (kể cả row version), dòng nhập, số dư tồn, số posting/công nợ và menu.

Chỉ dùng database SQL GUID dùng một lần, Web và Chrome headless/profile riêng thuộc fixture; launcher Chrome thường tắt trong test. Fixture đã kết thúc. Không đổi database cửa hàng hoặc tác động cửa sổ/tab Chrome đang mở.

## Giới hạn nghiệm thu

Chưa có Android/iOS thật kết nối để kiểm tra bàn phím, safe area và thao tác chạm. Chi tiết phiếu, scanner/camera, quy cách và command/revision thuộc UI-05b–e. Các ca thiết bị/menu/ghim/F5-Stop UI-02/UI-03 còn trạng thái riêng, không được đánh dấu hoàn tất bằng lượt test này. Test UI-05a chỉ xem/lọc/mở form, không gửi tạo/sửa/xóa hoặc ghi nhận hàng.

## Bằng chứng

- [Runtime review](mobile-ui-20261025/evidence/ui-05a/runtime-review.json), [source review](mobile-ui-20261025/evidence/ui-05a/static-review.json), [ma trận nghiệm thu](mobile-ui-20261025/evidence/ui-05a/acceptance-matrix.json), [baseline delta](mobile-ui-20261025/evidence/ui-05a/baseline-delta.json).
- [7 bài hồi quy](../../TestResults/mobile-ui-05a-20261025/regression/regression.trx), [28 nhóm browser](../../TestResults/mobile-ui-05a-20261025/browser/browser-results.json), [log xác nhận SQL giữ nguyên](../../.artifacts/ui-05a/browser-final.log).
- [Điện thoại 390 px](../../TestResults/mobile-ui-05a-20261025/browser/receiving-phone-390.png), [320 px](../../TestResults/mobile-ui-05a-20261025/browser/receiving-phone-320.png), [form tạo phiếu](../../TestResults/mobile-ui-05a-20261025/browser/receiving-create-phone-390.png), [chữ 150%](../../TestResults/mobile-ui-05a-20261025/browser/receiving-text-150-percent-390.png).
- [Máy tính hiện tại](../../TestResults/mobile-ui-05a-20261025/browser/receiving-desktop-current.png), [máy tính khi tắt CSS phone](../../TestResults/mobile-ui-05a-20261025/browser/receiving-desktop-baseline.png).
