# Kiểm thử ACB trên local và host

Giữ URL callback đã đăng ký và chọn cửa hàng bằng StoreId tại **Cài đặt thanh toán ACB → Chọn cửa hàng nhận callback từ URL cũ** (quản trị toàn hệ thống), hoặc `/admin/acb/callback-routing`. Cần migration `20260909062834_AddAcbCallbackStoreRouting`. Lựa chọn lưu vào database, không phụ thuộc subdomain. Hướng dẫn và bài test R01–R06: [acb-callback-store-routing.md](acb-callback-store-routing.md).

Bản hiện hành có lịch sử **Phương án xác nhận** tại hóa đơn → Chuyển khoản, gồm nguồn đầu tiên xác minh thành công, thời điểm, người thao tác hoặc mã biên nhận callback. Cần migration `20260909024821_AddAcbConfirmationAudit` sau bản nhiều lần thanh toán; script `docs/acb-confirmation-audit-upgrade.sql`. Thử L23–L27 trong phiếu nghiệm thu: để đếm giờ nhận tiền, kiểm tra bằng tay cho khoản khác, refresh không đổi nguồn và xem giao dịch cũ chưa lưu nguồn. Callback thật chỉ kết luận qua biên nhận trên host; nguồn ghi Tự kiểm tra theo thời gian vẫn có thể xác nhận tiền trên localhost.

Bản 09/09 bổ sung nhiều lần thanh toán QR: số tiền mặc định là phần còn thiếu, có thể nhập từng phần. QR thủ công dùng tài khoản mặc định; cả hai luồng có Tạo QR mới và Mở lại QR đã lưu. Áp dụng migration `20260909015242_AddPosQrInstallmentLinks` khi chạy lại. Bài test L14–L21 trong `docs/acb-acceptance-tests.md` kiểm tra trả từ nhiều tài khoản, xác nhận lặp và chỉ chốt/in khi đủ tiền.


Phiếu nghiệm thu hiện hành, gồm danh sách giao dịch cuối ngày và hồi quy lỗi định dạng số: [acb-acceptance-tests.md](acb-acceptance-tests.md).

## Người dùng thử nhận tiền tại POS

1. Mở đúng ca và máy đang giữ đơn. Đơn thử 15 có tổng 9.000đ, tiền mặt 4.000đ; người dùng đã chuyển 5.000đ vào QR GA0000000006 và ACB trả giao dịch 15100 COMPLETED. Không chuyển thêm tiền hoặc tạo QR khác cho lần thử này. Sau khi chạy bản mới: **Thanh toán → Mở lại QR gần nhất → Kiểm tra ngay**, hoặc chọn GA0000000006 trong Lịch sử QR. Không cần nhập số tiền hay Enter để mở lại mã.
2. Người dùng tự quét và chuyển số tiền của QR. Sau khi ngân hàng báo chuyển thành công, bấm **Kiểm tra ngay** hoặc **F9** tại POS. Có thể hệ thống đã kiểm tra dự phòng và cập nhật trước lúc bấm.
3. Nếu ACB xác nhận đủ tiền: hệ thống giữ khoản tiền mặt, thêm một khoản ACB, chốt đúng đơn và chỉ cấp một yêu cầu in bill. Nếu chưa thấy giao dịch, thông báo thời điểm tra cứu và giữ đơn chờ; có thể kiểm tra lại. Số tiền lệch, nhiều giao dịch, tiền về sau hủy, sai ca/máy hoặc đơn đã sửa chuyển sang cần đối chiếu.
4. Vào **Danh sách đơn → Chuyển khoản ACB → Tra cứu ACB** để xem số tiền thực nhận, mã giao dịch, nội dung, ngày hạch toán, lịch sử QR. Nút cùng chức năng cũng có ở chi tiết hóa đơn. Việc tra cứu không tự gọi endpoint chốt/in trên trang hóa đơn.
5. Có thể kiểm tra riêng trường hợp chưa chuyển tiền rồi bấm Kiểm tra ngay: đơn vẫn nháp, không thêm payment. Hủy QR chỉ bỏ lần thanh toán QR, giữ tiền mặt đã thu.

## Callback khi lên host

- URL đã đăng ký: `https://www.gaomart.com.vn/Admin/api-callback`; phương thức POST, Content-Type application/json, header **x-api-key** chứa khóa đã đăng ký. Code hỗ trợ cả **Thông báo trạng thái giao dịch QR Code** và **Thông báo danh sách giao dịch QR Code**, gồm TRANSACTION_UPDATE/TRANSACTION_HISTORY. Cần ACB kích hoạt các dịch vụ tương ứng; lưu cài đặt trong GaoApp không tự đăng ký dịch vụ với ACB.
- URL www cần HTTPS hợp lệ và reverse proxy/IIS chuyển POST, nội dung, x-api-key, host gốc vào ứng dụng. Tại màn chọn cửa hàng nhận callback, lưu đúng StoreId cho URL này. Không cần đổi subdomain cửa hàng thành www. Cấu hình proxy tin cậy và scheme đúng để tránh chuyển hướng callback. Các subdomain khác ánh xạ cửa hàng riêng.
- Áp dụng migration ACB/inbox đang có trong repository; cấu hình tài khoản/khóa theo từng cửa hàng, giữ Data Protection keys khi triển khai nhiều máy hoặc khởi động lại để giải mã được cấu hình đã lưu.
- Cấp quyền ghi `App_Data/Logs/acb-callback` cho tài khoản chạy ứng dụng. Nếu dùng IIS, bật ứng dụng luôn chạy/phù hợp hosting plan để hàng đợi nền tiếp tục xử lý. Khi máy chủ khởi động lại, inbox chưa xử lý được quét lại.
- Thử một QR và chuyển tiền do người dùng thực hiện, giữ POS gốc mở để nhận hub/chốt/in. Kiểm tra Callback gần nhất và **Chẩn đoán callback** trong Cài đặt ACB, sau đó đối chiếu trạng thái hóa đơn.

## Đọc lỗi

| Dấu hiệu / mã | Nơi cần kiểm tra |
|---|---|
| Không có callback trong log ứng dụng | ACB đã kích hoạt gửi hay chưa, URL/DNS/HTTPS, log IIS/reverse proxy |
| HTTPS_REDIRECT | Scheme/proxy hoặc ACB đang gọi HTTP thay vì HTTPS |
| ROUTE_OR_STORE_NOT_FOUND | URL hoặc ánh xạ subdomain/cửa hàng |
| CALLBACK_STORE_NOT_SELECTED | Chưa chọn StoreId nhận URL cũ hoặc đang tạm ngưng; mở trang chọn cửa hàng bằng tài khoản quản trị toàn hệ thống |
| CALLBACK_STORE_NOT_FOUND | StoreId đã chọn bị xóa hoặc ngừng hoạt động; không tự chuyển sang cửa hàng khác |
| AUTH_REJECTED / CONFLICTING_AUTH_HEADERS | Tên header x-api-key, khóa đã đăng ký, header có bị proxy bỏ/ghi đè không |
| CONFIG_DECRYPT_FAILED | Data Protection keys và cấu hình bí mật khi chuyển máy/host |
| INVALID_JSON / INVALID_MASTER_META / INVALID_PAGINATION / INVALID_TRANSACTION_FIELDS | So payload ACB gửi với hợp đồng callback; đối chiếu bản tin theo clientRequestId mà ACB cung cấp |
| UNSUPPORTED_NOTIFICATION | Kiểm tra requestType NOTIFICATION và requestCode TRANSACTION_UPDATE hoặc TRANSACTION_HISTORY |
| INBOX_SAVE_FAILED / DATABASE_WRITE_FAILED | Migration, quyền database, kết nối SQL |
| ACB_TOKEN_FAILED / ACB_REQUEST_FAILED / ACB_######## | Token/endpoint, đường truyền tới ACB, mã nghiệp vụ ACB |
| BANK_RESPONSE_INVALID_JSON | Phản hồi tra cứu ACB/proxy không phải JSON; hàng đợi giữ để thử lại |
| AWAITING_BANK_EVIDENCE | Đã nhận callback nhưng tra cứu ACB chưa trả giao dịch tương ứng; hệ thống tiếp tục thử |
| UNMATCHED_QR | Có callback hợp lệ nhưng không ghép được QR tại cửa hàng; giữ bản tin để đối soát |

HTTP trả `X-Acb-Diagnostic-Id`; tìm giá trị này trong `App_Data/Logs/acb-callback/callback-YYYYMMDD.jsonl`, rồi dùng ReceiptId để nối với log xử lý nền. Bảng inbox và trang cài đặt có số lần thử, mã lỗi cuối và thời điểm xử lý. Không đưa API key, token hoặc payload chứa thông tin khách hàng vào thông báo trao đổi công khai.

Đặc tả ACB mô tả trường checksum nhưng trang Developer chưa nêu công thức ký/kiểm tra. Code lưu checksum cùng bản tin, xác thực bằng x-api-key và xác nhận tiền qua API retrieve có token; không tự đoán thuật toán checksum. Nếu ACB cung cấp thuật toán riêng thì bổ sung và kiểm thử trước khi kết luận phần này đã được xác minh.

## Chức năng ACB có thể bổ sung, chưa triển khai

- Tra cứu giao dịch theo khoảng ngày và merchantId để lập bảng đối soát của từng cửa hàng; mở rộng từ chức năng đang tra theo một hóa đơn.
- Đối chiếu với ACB về cơ chế chặn thanh toán trùng/sai số tiền đối với NAPAS tức thì nếu cửa hàng chưa được áp dụng. Hệ thống vẫn cần kiểm tra số tiền và giao dịch đến muộn ở phía mình.

Nguồn: [Thông báo trạng thái QR](https://developer.acb.com.vn/acb/open/vi/product/10489/api/8178), [Danh sách giao dịch tức thì/cuối ngày](https://developer.acb.com.vn/acb/open/vi/product/10489/api/8175), [Truy vấn giao dịch](https://developer.acb.com.vn/acb/open/vi/product/10489/api/9765).


## Điều chỉnh tra cứu chuyển khoản và ô x-api-key (09/09/2026)

- Danh sách hóa đơn và chi tiết hóa đơn chỉ hiện nút **Chuyển khoản / Thông tin chuyển khoản** khi có khoản thanh toán BankTransfer còn hiệu lực hoặc lịch sử tạo QR của chính cửa hàng. Đơn chỉ tiền mặt/thẻ không có nút. Lịch sử QR đã hủy vẫn xem được để đối chiếu.
- Tra cứu trả thêm số tiền hóa đơn, số tiền chuyển khoản đã ghi nhận, khoản còn thiếu; phân biệt giao dịch ACB với khoản thu do thu ngân ghi nhận. QR thủ công cũng có lịch sử riêng. Không suy ra ngân hàng đã xác nhận chỉ từ tên nhà cung cấp ACB.
- Popup mới dùng bảng giao dịch và các dòng lịch sử QR có thể mở chi tiết. Không cộng các QR đã hủy/thử tạo vào tiền đã thu. Mã giao dịch và nội dung từ ngân hàng được hiển thị bằng textContent.
- Trong **Cài đặt thanh toán ACB**, nhóm **Callback ACB · Xác nhận tự động** có URL callback, header cố định **x-api-key**, ô mật khẩu **Giá trị x-api-key** và trạng thái đã lưu. Ô này dùng lại CallbackApiKey hiện có, được lưu mã hóa. Để trống giữ khóa cũ. Không có migration mới cho lần điều chỉnh này.
- Kiểm thử sau sửa: 108 ca .NET thuộc AcbPaymentTests, PosCheckoutPaymentUiContractTests, GlobalExceptionMiddlewareTests; 6 ca Node POS realtime. Thêm các tình huống đơn tiền mặt/thẻ, CK thủ công, QR đang chờ/đã hủy, QR thủ công, khoản CK đã xóa, cách ly cửa hàng và chống hiển thị trùng khoản ACB tự động.
- Kiểm tra trực quan localhost: trong 15 đơn có 2 đơn hiện nút chuyển khoản (đơn 15 có lịch sử ACB; đơn 2 có chuyển khoản OCB thủ công 20.000đ). Popup đọc đúng dữ liệu, hiển thị lịch sử QR đã hủy và không cộng vào tiền đã thu. Trang cài đặt đang chạy vẫn là view cũ của bản Release; cần build/chạy lại bản mới để thấy nhóm x-api-key. Chưa thử khách chuyển tiền thật; callback Internet vẫn chờ host.

Đã triển khai API **Thông báo danh sách giao dịch QR Code** (8175), gồm TRANSACTION_HISTORY cuối ngày. Màn `/admin/acb/reconciliation` lọc theo ngày hiệu lực/loại bản tin, hiển thị các khoản cần đối chiếu và tiến độ nhận trang. Migration mới `20260908192844_AddAcbQrNotificationReconciliation` đã có trong source. Kích hoạt dịch vụ và callback thật qua Internet vẫn cần nghiệm thu trên host.
