# Bộ chọn sản phẩm theo tồn chứng từ — 29/10/2026

Đã nâng cấp hộp “Thêm sản phẩm vào hóa đơn” tại /Admin/Invoice/Detail theo phương án được người dùng chốt. Hóa đơn thông thường chỉ gợi ý sản phẩm còn khả dụng ở đúng kho và còn lượng có thể thêm; từng kết quả và sản phẩm đã chọn đều hiện số lượng/đơn vị/kho. Quyền, menu và cấu hình cửa hàng giữ nguyên.

## Cách hoạt động

- Dùng nguồn biến động của invoice-input-stock, không dùng InventoryBalance hoặc chỉ dựa vào cờ HasInputInvoice. Còn khả dụng = tổng biến động chứng từ − lượng đang giữ, cùng sản phẩm/kho/pháp nhân.
- Kho của dòng thêm mới được xác định bằng chính bộ phân giải tồn trước phát hành: kho mặc định pháp nhân → kho nguồn đơn → kho mặc định cửa hàng; chỉ dùng kho duy nhất khi cấu hình cũ xác định rõ được. Không tìm được kho hợp lệ thì trả thông báo, không gộp kho hay đoán từ tồn sản phẩm.
- Tìm theo tên/SKU/barcode, hỗ trợ tên không dấu trên SQL Server. Chỉ tìm ID trước, đọc tồn theo lô sản phẩm/kho, lọc lượng dương rồi mới lấy tối đa 20 kết quả. Không nạp ảnh/graph POS hoặc chạy một truy vấn tồn cho từng sản phẩm.
- Với sản phẩm đã có trong hóa đơn, hiển thị “Đã có trong HĐ” và “Có thể thêm”. Giới hạn thêm quy về đơn vị gốc, dùng quy đổi hiện có của dòng cũ; ghi nhận phần giữ/xuất lịch sử của chính hóa đơn đúng một lần để tránh trừ trùng.
- Khi chọn sản phẩm, đọc lại tồn mới. Khi lưu, kiểm tra lại trên server trong transaction/khóa store của chức năng sửa chi tiết hiện có; không tin SL từ trình duyệt. Hết/thiếu tồn thì từ chối rõ ràng, không cắt SL. Nếu lưu báo lỗi, đọc lại SL và giữ nguyên số lượng/giá người dùng đã nhập.
- Hủy tìm cũ khi gõ tiếp, đổi lựa chọn hoặc đóng popup; phản hồi muộn không được khôi phục lựa chọn cũ. Phân biệt hết kết quả với lỗi tải; 401/403 yêu cầu tải lại. Hai API đọc dùng no-store và bắt buộc đúng hóa đơn/cửa hàng hiện tại.

Hóa đơn điều chỉnh/thay thế giữ luồng chọn hàng hiện có, không tiêu tồn lần hai. Dòng nhập tay không gắn sản phẩm vẫn dùng được. Không đổi nghiệp vụ sửa/xóa dòng cũ, tính VAT/tiền, phát hành Viettel hay các view khác. Số lượng trong bộ chọn theo ĐVT gốc; không thêm bộ chọn quy đổi mới.

## Kiểm thử

**877/877 ca .NET và 721/721 ca JavaScript đạt.** Có 875 ca hồi quy hóa đơn/UI/boundary, một nghiệm thu repository/command SQL và một nghiệm thu Chrome. Các ca chạy lặp được loại trùng khi đếm. Build 0 lỗi, một cảnh báo xUnit2031 có sẵn ở lần build test.

SQL thật trên database GUID riêng có 2.200 sản phẩm khớp từ khóa, chỉ ba sản phẩm đủ điều kiện; lượt tìm đầu mất 1076 ms trong fixture cục bộ. Đây là số đo của dữ liệu kiểm thử, không phải cam kết thời gian trên cửa hàng thật. Đã đối chiếu số lượng với invoice-input-stock; thử tồn 0/âm/lẻ, hàng đang giữ/đã phát hành, hàng ở kho khác, hàng inactive/deleted, hơn 2.100 ID, quy đổi dòng cũ và outbound legacy. Hai yêu cầu cùng thêm lượng cuối vào một hóa đơn chỉ có một yêu cầu thành công.

Chrome headless riêng kiểm tra giao diện 1440/320/390/430 px, tên/kho/SL sau chọn, chặn vượt SL ở client và server, thêm một dòng hợp lệ, đổi từ khóa xóa liên kết cũ, phản hồi tìm đến muộn, hết tồn khi chọn, lỗi 503, thay đổi quyền 403 và giữ catalog của hóa đơn điều chỉnh. Trường hợp lưu báo tồn thay đổi đã xác nhận cập nhật SL mà không ghi đè giá/SL vừa nhập. Không có pageerror hoặc request ra ngoài fixture. Không đóng các cửa sổ Chrome đang mở.

Kiểm tra HTTP thật: tài khoản không có quyền quản lý hóa đơn nhận 403; cửa hàng khác không đọc được hóa đơn/số lượng của cửa hàng này; context host không có StoreId bị từ chối. Snapshot giữ nguyên cửa hàng/menu/ghim/ẩn-hiện/vai trò/quyền, kho/pháp nhân/provider, đơn/thanh toán, tồn vật lý và tồn chứng từ, tự động phát hành, rewards/voucher. Chỉ thêm đúng dòng manual do test yêu cầu: tổng SL hóa đơn 2 → 3, tổng tiền 56.000 → 84.000 đồng. Không gọi lệnh phát hành/provider.

## Gói source

Gói WebOnly Production mới: E:\GaoApp\GAOAPP-SA-20260819-01\publish\releases\20261029-085835-533fd187

Manifest SHA-256: F33C538258F81EB80088487495AE365BADABAA66B44DD6432FE3495607E215F3

Ghi nhận đủ 2078 file C# production trong đầu vào compile; không filter/shadow source. Hiện có 285 view và 445 asset riêng. Bộ kiểm tra integrity/exclusion của gói đạt. Runtime nghiệm thu dùng Web Development/SQL riêng; gói Production được build/kiểm tra integrity riêng, chưa deploy IIS.

Không có migration mới/pending model. Build/chạy lại bằng Visual để áp dụng source này; không cần cập nhật DB. Nghiệm thu host/thiết bị thật và phần hết hạn voucher tiếp tục để sau như đã thống nhất.

[Ảnh máy tính](../../TestResults/invoice-product-picker-20261029/picker-1440.png) · [Ảnh điện thoại](../../TestResults/invoice-product-picker-20261029/picker-390.png) · [SL sau khi chọn](../../TestResults/invoice-product-picker-20261029/selected-phone.png) · [Bằng chứng](mobile-ui-20261025/evidence/invoice-product-picker-20261029/runtime-review.json)
