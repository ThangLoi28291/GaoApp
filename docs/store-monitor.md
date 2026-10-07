# Giám sát trực tiếp tại văn phòng

Trang `/admin/store-monitor` dành cho màn hình admin của **cửa hàng đang đăng nhập**. Quyền riêng `admin.storemonitor.view` cho phép xem hoạt động của cả cửa hàng; cần cấp quyền này cho người quản lý được phép theo dõi nhân viên. Menu hệ thống có mục “Giám sát trực tiếp”. Trang hỗ trợ toàn màn hình, nền sáng/tối, tắt hiệu ứng và tùy chọn giảm chuyển động của hệ điều hành.

Mặc định dùng nền sáng. Khi ít việc, POS nằm trên cùng và chia từng quầy; khi các khu vực khác có nhiều việc, màn tổng dùng lưới các khu vực ngang nhau và các ô công việc trong từng khu vực. Mật độ tự điều chỉnh, luồng cập nhật ở bên phải. Không cắt danh sách bằng khung cuộn riêng hoặc giới hạn một ô ngoài POS. Bài kiểm tra 1920×1080 hiển thị đồng thời 15 công việc, 3 ở mỗi khu vực đã kết nối. Số việc nhiều hơn hoặc nội dung dài có thể làm trang cần cuộn; mọi ô vẫn được giữ, không cam kết số việc tùy ý đều vừa một khung. Tên nhân viên có trong từng ô; danh sách nhân viên tổng hợp ẩn khi cần chỗ. Quyền/menu không đổi.

## Dữ liệu đang kết nối

- Nhập hàng: tạo phiếu và cập nhật mặt hàng/dòng nhập.
- POS: tạo đơn, cập nhật giỏ hàng, thanh toán, chốt/giữ/mở lại/hủy/hoàn đơn.
- Kho: cập nhật kiểm kê, chuyển kho và xác nhận chứng từ.
- Tem: gửi lệnh in, xác nhận/hủy và cập nhật kế hoạch in.
- Duyệt phiếu: gửi duyệt, duyệt/từ chối phiếu nhập, kiểm kê và cập nhật giá nhập.

“Đang thao tác” là tín hiệu tương tác với màn hình nghiệp vụ, không phải bằng chứng nhân viên đang thực hiện công việc vật lý. Không thu nội dung gõ, ảnh màn hình hay vị trí. Mở trang mà không tương tác chỉ hiện “Đang mở màn hình”. Các lần lưu thành công được máy chủ ghi vào luồng sự kiện; lỗi, yêu cầu bị từ chối, đọc/tra cứu và xem trước không tạo sự kiện. Trạng thái thao tác hết hạn sau 15 giây không có tín hiệu thao tác mới; kết nối nhân viên hết hạn sau 60 giây không có heartbeat. Tab ẩn/đóng gửi tín hiệu rời trang.

Presence được nhóm theo **nhân viên + khu vực + mã quầy + phiếu**, chỉ gộp các tab cùng nhóm. Nhiều nhân viên nhập hàng cùng lúc có nhiều ô; một người mở hai phiếu cũng có hai ô. Kiểm kê và chuyển kho có cùng số ID vẫn là hai công việc khác nhau. Một tài khoản mở ba quầy vẫn hiện ba quầy; cùng người mở nhập hàng và POS sẽ có ở cả hai khu vực. POS phản ánh quầy/giỏ hiện tại theo terminal, các nghiệp vụ chứng từ phản ánh từng người/phiếu. Vị trí quầy giữ theo thứ tự mã khi rời trang. Tổng quan đếm số nhóm công việc đang mở và số tài khoản riêng biệt; dùng chung tài khoản không xác định được số người thực tế.

Mã công việc trong presence được máy chủ kiểm tra cùng Store và ký vào ticket. Client không được tự khai ID/tên phiếu. Trang danh sách không có phiếu chỉ hiện ngữ cảnh màn hình chung. Với in tem, chuyển phiếu bằng XHR nhận ticket từ response được cấp quyền; đóng phiếu hoặc đổi sang khu vực in khác bỏ context phiếu đó. Đổi phiếu reset giờ mở; đóng một tab không xóa công việc ở tab khác. Các lần lưu có work key của chứng từ cha; dòng kiểm kê/chuyển kho được tra parent read-only trước khi sửa/xóa. Không thêm migration hoặc thay đổi business posting.

Mỗi ô công việc có tên nhân viên/quầy, thao tác cụ thể, mã chứng từ và chi tiết từ lần lưu thành công. Ngoài POS, máy chủ tra dữ liệu đã lưu trong đúng cửa hàng để lấy tên hàng, đơn vị và phiếu cha; trước sửa/xóa giữ snapshot nhỏ để thể hiện lượng trước/sau hoặc hàng vừa bỏ. Không dùng tên/giá do client tự khai để thay thế dữ liệu đã lưu. Dòng có đơn vị snapshot trống được tra đơn vị đã liên kết; dòng nhập dùng đơn vị gốc khi hệ số là 1 và chưa có đơn vị riêng. Không đoán đơn vị khi chưa xác định được. Khi không tra được dữ liệu, dùng thông báo nền và ghi log; giám sát không làm lỗi thao tác nghiệp vụ.

- Nhập hàng: “vừa quét nhập Gạo ST25”, số lượng và đơn vị; sửa “5 kg → 8 kg”; bỏ hàng vẫn hiện tên/lượng đã xóa. Intake ghép đúng CommandId với lần nhận hàng và itemId với hàng sửa/duyệt/xóa; lưu thông tin chờ duyệt được phân biệt với duyệt hàng.
- Kiểm kê: tên hàng, lượng đã đếm kể cả 0, lượng trước/sau và chênh lệch theo đơn vị gốc. Chuyển kho: tên hàng/lượng, kho nguồn → kho đích; gửi duyệt/từ chối cũng có nội dung phiếu.
- In tem: tên hàng và số tem từng hàng. Gửi in hiện số đã gửi; xác nhận hiện số thực nhận/tổng gửi, kể cả thiếu hoặc 0. Kế hoạch hiện số cần in mỗi hàng; hoàn tất phiếu khác với xác nhận một lệnh in. Tên hàng lấy từ nội dung lệnh đã lưu, có thể khác tên trên phiếu nhập cũ.
- Duyệt/lưu giá: loại phiếu, số phiếu, số mặt hàng và tối đa hai tên đại diện. Giá nháp chỉ đếm các dòng server xác nhận đã lưu; không phát request prices, ảnh, thông tin khách/nhà cung cấp hoặc nội dung ghi chú. Header có đổi kho/ngày phiếu thì hiện trước/sau; cước vận chuyển chỉ ghi khi trang trả thông báo lưu thành công, lỗi chuyển hướng không tạo sự kiện.

Giờ lưu dạng `HH:mm:ss` và số giây/phút đã trôi qua cập nhật liên tục theo giờ máy chủ. Giờ mở màn hình và thời gian màn hình đã mở được ghi riêng; bộ đếm này không phải thời gian làm một công việc cụ thể. Tên đại diện dài được rút gọn, tổng số mặt hàng vẫn được giữ; nội dung registry tối đa 240 ký tự. Mỗi người/phiếu giữ giờ và hiệu ứng riêng như trước.

Hiệu ứng đã duyệt gồm phiếu nhập có bút và dòng dữ liệu đang ghi, tia quét mã POS, kiện hàng trên băng chuyền kho, tem chạy ra máy in và dấu duyệt phiếu. Hình động được căn giữa từng ô, co theo mật độ màn tổng; nền tối giữ hình và chữ rõ. Sau lưu có trạng thái “Vừa lưu xong” trong tối đa 12 giây với dấu xác nhận hiện một lần, kể cả khi nhân viên đã rời trang. Lần lưu mới hơn tín hiệu thao tác được ưu tiên hiện xác nhận; khi nhân viên thao tác mới sau lần lưu, ô trở lại hiệu ứng đang làm. Một lần lưu tiếp theo phát lại dấu xác nhận riêng của ô đó. Sau đó giữ nội dung lần lưu gần nhất; khi chờ thao tác hình dừng lại, không có animation chờ lặp. Animation không báo rằng hàng đã giao, máy in vật lý đã chạy hay phiếu đã hoàn tất ngoài trạng thái endpoint xác nhận.

Khu vực giao hàng hiện báo **Chưa kết nối**. Cần có nguồn trạng thái giao hàng thực tế để nối tiếp; không phát sinh xe di chuyển hoặc trạng thái giao hàng giả. Số lượng, tỷ lệ tiến độ và tổng kết cả ngày chưa được suy ra từ các thao tác.

## Vận hành

SSE `/admin/api/store-activity/stream` chuyển cập nhật tối đa mỗi giây. Máy chủ kiểm tra lại phiên đăng nhập, cửa hàng và quyền hiện tại trước khi gửi dữ liệu; heartbeat tối đa 5 giây. Khi mất mạng, màn hình dừng hiệu ứng, đánh dấu tín hiệu cũ và tự nối lại. Thu hồi quyền đóng luồng. `/snapshot` phục vụ kiểm tra trạng thái. Presence chỉ chấp nhận ticket bảo vệ bằng Data Protection do trang được cấp quyền phát hành, gắn với nhân viên, cửa hàng và module; POST cần antiforgery.

Đây là bộ nhớ giám sát tạm thời trên **một Web instance**: tối đa 120 sự kiện trong 4 giờ, 200 tab/cửa hàng, 8 tab/nhân viên, 8 màn hình giám sát/cửa hàng và 512 cửa hàng trong bộ nhớ. Khởi động lại làm mới luồng gần đây; chứng từ và audit nghiệp vụ vẫn ở SQL. Không dùng bộ đếm này làm báo cáo cả ngày. Khi triển khai nhiều instance, cần thay registry bằng kho trạng thái/pub-sub dùng chung trước khi bật tính năng trên nhiều máy chủ. Không cần migration cho tính năng này.

Kiểm tra: `StoreActivityTests`, `StoreActivityDetailsTests`, `StoreActivityEnricherTests`, `StoreMonitorHttpTests`, kiểm tra bảo mật endpoint và browser probe `store-monitor.browser.cjs`. [Báo cáo nền sáng/animation](development/STORE-MONITOR-UPGRADE-20261006.md), [báo cáo nhiều công việc](development/STORE-MONITOR-MULTIWORK-20261006.md), [áp dụng animation đã duyệt](development/STORE-MONITOR-ANIMATION-20261006.md) và [thông báo chi tiết](development/STORE-MONITOR-DETAILS-20261006.md) phân biệt Web/SQL thật với snapshot mô phỏng dùng để kiểm tra bố cục/animation toàn bộ khu vực.
