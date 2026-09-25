# Hóa đơn nháp GaoStore dùng cấu hình đang hoạt động

Theo yêu cầu người dùng sau diễn tập, Web sử dụng cấu hình Viettel đang hoạt động của cửa hàng cho hóa đơn GaoStore ở LocalDraft/ReadyToIssue/Previewed, chưa có số/ngày phát hành/mã giao dịch nhà cung cấp và không thuộc nhóm LegacyReadOnly. Không cần SQL ánh xạ từng bản nháp. Quy tắc chọn cấu hình đang hoạt động theo cửa hàng giữ như repository hiện có (Id mới nhất nếu có nhiều cấu hình đang hoạt động).

- Xem JSON lấy MST, loại, mẫu, ký hiệu và các lựa chọn từ cấu hình hiện tại. Nếu chưa có UUID, tạo mã chỉ dùng cho bản xem trước; không ghi InvoiceHeads hay cho tra cứu UUID tạm đó.
- Khi gửi, chọn cấu hình một lần và tạo payload từ cùng cấu hình/UUID; kiểm tra tồn trước khi lưu thông tin gửi cùng trạng thái Issuing. API nhận cùng MST và JSON đã chọn.
- Giữ nguyên snapshot nguồn GaoStore; không sửa lại hóa đơn đã phát hành/đã bắt đầu gửi hoặc nhóm chỉ tra cứu để theo cấu hình mới. Retry/tra cứu sử dụng liên kết và thông tin phát hành đã lưu.
- Không đổi schema hoặc SQL migration; không cần chạy lại reset/import. Chỉ build và chạy lại Web. Mã nguồn ZIP dự phòng đã được đồng bộ sửa đổi này; các công cụ đọc migration vẫn chỉ kiểm tra dữ liệu.
- 49 kiểm thử đạt (LegacyInvoiceConfigurationTests và InvoiceInputStockRepositoryTests), gồm luồng dịch vụ thực với database trong bộ nhớ và client giả. Không kết nối SQL đang dùng, không gọi API Viettel.

Thử lại: lưu cấu hình đang dùng ở Admin/InvoiceProviderSetting, dừng Web đang debug, build/chạy lại bằng Visual Studio, mở hóa đơn nháp và Xem JSON Viettel. Kiểm tra MST/mẫu/ký hiệu theo cấu hình vừa lưu. Phát hành thật vẫn cần anh chủ động thao tác sau đối chiếu.
