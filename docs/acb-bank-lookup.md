# Tra cứu ACB trực tiếp tại trang đối soát

Đường dẫn: `/admin/acb/reconciliation`. Chọn ngày bắt đầu và ngày kết thúc, rồi bấm **Tra cứu ACB**. Khoảng ngày tối đa 31 ngày tính cả hai đầu. **Xem callback** giữ chức năng xem thông báo đã nhận; lựa chọn loại callback không lọc kết quả ngân hàng.

## Hai nguồn dữ liệu

- **Tra cứu trực tiếp ACB — Nguồn: API ngân hàng:** gọi ngân hàng tại thời điểm bấm nút, hiển thị khoảng ngày, thời điểm lấy theo giờ Việt Nam, môi trường, MerchantId, mã QR, mã giao dịch, ngày thanh toán, số tiền và trạng thái. Có liên kết tới đơn POS của cửa hàng khi ghép được mã QR.
- **Callback đã nhận — Nguồn: thông báo ACB gửi về:** giữ dữ liệu biên nhận đã lưu trong database và các bước đối soát hiện có. Cột callback ở bảng tra cứu chỉ đếm biên nhận thực tế đã nhận cho mã QR tại cửa hàng, kể cả thông báo tức thời và cuối ngày.

Tra cứu trực tiếp không tạo callback, không gọi hàng đợi xử lý callback, không ghi OrderPayment và không chốt hoặc in đơn. Giao dịch thành công tại ACB vẫn có thể có cột **Chưa nhận callback**. Điều này giúp phân biệt ngân hàng đã ghi tiền với đường gửi thông báo chưa hoạt động.

## API và phân trang

Đặc tả đã kiểm tra trong phiên ACB Developer ngày 12/10/2026: [Truy vấn giao dịch QR Code 1.0.5](https://developer.acb.com.vn/acb/open/vi/product/10489/api/9765).

Server sử dụng cấu hình ACB của cửa hàng hiện tại và gọi `GET /acb/open/payments/qr-payment/v1/retrieve`, truyền `fromDate`, `toDate`, `merchantId`, `page`, `size`. Trang ngân hàng bắt đầu từ 0; giao diện bắt đầu từ 1. Mỗi yêu cầu lấy 50 QR. Tổng QR lấy từ `responseBody.pagination.totalRows`; tổng trang từ `totalPages`. Chi tiết tiền là `orders[].transactionDetail[]`, không suy ra tiền đã nhận từ giá trị yêu cầu của QR.

Các số giao dịch và tổng tiền thành công có chú thích **trên trang này**. Không cộng với thống kê callback hoặc mô tả thành tổng của cả khoảng ngày. Có Trước, Sau và Đến trang để đi trực tiếp đến trang cần xem; chỉ tải trang được chọn.

## Quyền và lỗi

Endpoint tra cứu: `POST /admin/acb/reconciliation/bank-lookup`. Yêu cầu đăng nhập, quyền `pos.payment.view` như trang đối soát, anti-forgery token và tài khoản ngân hàng đang hoạt động thuộc cửa hàng. Phản hồi không được cache. Client secret/access token chỉ được sử dụng trên server, không gửi về trình duyệt.

Lỗi mạng, xác thực, quá thời gian chờ hoặc cấu trúc ACB không hợp lệ hiển thị lỗi tra cứu; không hiển thị như kết quả rỗng thành công. Đổi khoảng ngày sẽ hủy yêu cầu cũ và xóa kết quả cũ, tránh hiển thị nhầm ngày. Nội dung ngân hàng được Razor mã hóa trước khi hiển thị.

Không cần migration hoặc tạo menu mới. Publish bản Web mới theo quy trình hiện có, giữ cấu hình và Data Protection trên máy chủ. Kiểm thử thật sau publish bằng một khoảng ngày có giao dịch QR đã thanh toán; so sánh kết quả trực tiếp với biên nhận callback riêng.
