# UI-10 — Hoàn tất triển khai và kiểm tra tự động

Ngày 26/10/2026. Đã triển khai toàn UI-10a/b/c/d/e cho điện thoại, còn nghiệm thu thiết bị thật. Bước tiếp theo: UI-11a, quản lý phiếu nhập.

## Giao diện đã nâng cấp

- UI-10a: danh sách yêu cầu mua có thống kê gọn, phạm vi của tôi/cửa hàng, tìm kiếm, thẻ kết quả và bộ lọc trượt dùng dữ liệu/quyền có sẵn. Form giữ tìm/quét, chọn đơn vị, hàng gõ tay, lượng và ghi chú từng dòng.
- UI-10b: duyệt từng mặt hàng bằng thẻ, đối chiếu tồn/đang về, lượng yêu cầu/duyệt/chênh lệch và lý do. Chuẩn bị đơn làm rõ dữ liệu đã khóa, nhà cung cấp, đơn vị mua, kho nhận và xác nhận cuối.
- UI-10c: danh sách, tạo/sửa và chi tiết đơn mua; đúng tên và nhãn ở mỗi quy cách khi cùng sản phẩm có nhiều dòng. Đã nhận/còn chờ/đóng thiếu, lý do, lịch sử và phiếu nhập liên quan dùng dữ liệu gốc. Hành động theo quyền, đặt sau nội dung để không che trường nhập.
- UI-10d: CommercialEdit chia thông tin nhà cung cấp/đơn vị mua/kho/ngày/ghi chú, giữ hàng đã duyệt khóa tên/đơn vị/lượng. Theo chính sách hiện tại, giá thực tế và VAT được nhập/chốt trên phiếu nhập kho; không thêm giá, thuế hoặc chiết khấu ngoài chức năng sẵn có. Quyền xem giá vốn giữ nguyên.
- UI-10e: bàn nhận hàng giữ tìm/quét quy cách, lượng nhận, đối chiếu từng dòng PO và hàng vừa nhận; hộp hoàn tất gửi quản lý rõ ràng. Giữ nguồn PO/phiếu nhập, lease, RowVersion và GUID thao tác cũ.

Chín view chỉ thêm hai asset mới. CSS dưới 768px dùng navy/blue, thẻ, input 16px, vùng chạm 44px và reduced motion; hộp thoại có vùng cuộn riêng và footer theo safe-area. Adapter chỉ gắn nhãn/role vào node gốc, xử lý nhãn bảng có rowspan và trả thuộc tính khi desktop; không clone form, tính lại lượng/tiền, lưu nháp riêng, fetch hoặc relay lệnh.

## Sửa lỗi số lẻ phát hiện khi test

Form thực trên máy chủ vi-VN đã đọc “2.5” thành “25”. Bổ sung Web model binder giới hạn bốn trường lượng của đề nghị/đơn mua để số từ HTML number/hidden giữ đúng ý nghĩa; hỗ trợ một dấu phẩy thập phân và từ chối dạng trộn dấu/không hợp lệ. Không đổi JSON, trường tiền, receipt binding hoặc chính sách application. Đã kiểm tra bảy ca số/vùng, sáu ca sai và một ca phạm vi provider; luồng form thực giữ đúng 2,5 qua lưu, duyệt và chuyển đơn. Sửa này cũng bảo đảm ý nghĩa số lẻ khi dùng form trên máy tính; bố cục máy tính không đổi.

## Kết quả kiểm tra

- Release build: **0 cảnh báo, 0 lỗi**. **593/593 .NET** qua: 578 hồi quy mua/nhập hàng, hợp đồng UI và ảnh nhập; 14 test binder và 1 browser/SQL mới. **37/37 JavaScript** qua. Không bỏ qua test.
- **7 nhóm browser/API/SQL**, **15 trạng thái**, **105 lượt phone** tại 320/360/390/430/600/767px và 390×440; không tràn ngang, không ID trùng, giữ nguyên node, giá trị, disabled và chủ form khi đổi breakpoint.
- **30 đối chiếu desktop** tại 768/1440px: computed CSS và vị trí/kích thước từng node giống khi tắt stylesheet mới; không còn thuộc tính phone. **27 lượt hộp thoại/bộ lọc** ở màn nhỏ/thấp: nội dung cuộn được, footer nằm trong viewport.
- Form thật kiểm tra thêm/xóa hàng gõ tay, ghi chú, số lẻ, sai quá ba số lẻ, xác nhận duyệt/chuyển, thương mại khóa lượng, đơn trực tiếp bắt buộc lý do, duyệt đơn và đóng/mở thiếu từng dòng.
- SQL sau luồng: hai dòng nguồn vẫn đúng lượng/đơn vị/hệ số/ghi chú đã duyệt; một PO cho một PR. Nhận đúng **1,25 hộp ×6 =7,5 đơn vị gốc** vào dòng hộp của PO. Phiếu ở **PendingApproval**, tồn vẫn **100**, lượng đã nhận trên PO vẫn 0; chưa ghi sổ kho tại bước gửi quản lý. Không thêm phiếu/lệnh nhận trùng.
- View-only không có hành động ghi; thiếu CSRF bị 400, thiếu quyền bị 403. Cửa hàng khác: PR/PO 404, phiếu nhận trả lỗi nghiệp vụ 400 theo hợp đồng sẵn có; không lộ tên/snapshot sản phẩm. Menu, cửa hàng, quyền, giá danh mục và dữ liệu cửa hàng khác khớp fingerprint.
- **796 file bảo vệ** khớp snapshot. Các controller/service/repository/DTO, script nghiệp vụ và CSS desktop gốc giữ hash. Program chỉ đăng ký binder nêu trên. Không menu, seeder, migration, schema, endpoint hay quyền mới. Baseline 560 file không sửa: **487 không đổi /73 đã đổi qua các bước /0 mất**.

Đã rà ảnh danh sách, sửa/duyệt/chuẩn bị yêu cầu, đơn nhiều đơn vị, CommercialEdit, duyệt/đóng thiếu, tạo/sửa đơn trực tiếp, bàn nhận và các hộp xác nhận. Các ô thống kê được thu gọn và bảng cùng sản phẩm nhiều đơn vị giữ đúng nhãn. Kết quả cuối không có lỗi JS hoặc CSP.

## Môi trường và phần còn lại

Chỉ chạy Web/LocalDB GUID thử riêng, Chrome headless/profile mới, tắt launcher thường và chặn transport ngoài/thiết bị in thật. Database/cấu hình cửa hàng và các cửa sổ/tab Chrome đang mở không bị thay đổi. Test fixture có cờ bật receiving riêng; không bật tính năng trong cấu hình cửa hàng thật.

Còn Android/iOS thật, bàn phím, safe-area, zoom, một tay, camera và máy quét phần cứng. Bản in đơn mua và in thật theo UI-22; quản lý/duyệt thương mại/XML phiếu nhập theo UI-11. Các nghiệm thu trước vẫn giữ trạng thái đang mở.

[Runtime](mobile-ui-20261025/evidence/ui-10/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-10/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-10/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-10/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-10-20261026; snapshot/log: .artifacts/ui-10.
