# POS: tồn âm khi có hàng giữ cho đơn giao

Theo yêu cầu chủ ngày 09/10/2026, đơn giao áp dụng cùng chính sách tồn âm như POS hiện hữu. Trước sửa, kiểm tra `DELIVERY_STOCK_PROTECTED` chạy riêng trước kiểm tra tồn âm, nên vẫn chặn khi tồn còn lại thấp hơn lượng giữ, dù `AllowNegativeBalance` hoặc `Warehouse.AllowNegativeInventory` đã bật. Trường hợp thực tế có tồn 0, giữ 2 cho đơn giao và POS bán 1; POS đã ghi nhận thanh toán nhưng không chốt được.

`InventoryMovementService` kiểm tra cùng cấu hình ở cả nhánh tồn cũ và nhánh precision 1: chỉ chặn phần giữ giao hàng nếu cả executor và kho đều không cho phép tồn âm. POS giữ cách xuất bán hiện hữu (`AllowNegativeBalance = true`), vì vậy không bị phần giữ giao hàng chặn chốt. Executor giao hàng vẫn phải có chứng cứ nguồn/bàn giao hợp lệ.

POS không nhả, tiêu thụ hoặc sửa lượng giữ của đơn giao khác. Số lượng tồn thực ghi nhận theo bút toán xuất; phần thiếu tiếp tục dùng cơ chế giá vốn tạm tính/Unknown và hậu kiểm hiện hữu. Các kiểm tra nguồn FIFO, phạm vi cửa hàng, duyệt, khóa và idempotency không đổi. Không có migration, không sửa chứng từ hoặc lịch sử kho đang chạy để bỏ lỗi.

Thanh toán đã ghi nhận vẫn nằm trên đơn. Sau cập nhật ứng dụng, nhân viên chốt lại đơn hiện tại; không cần thu tiền lại chỉ vì lần chốt trước bị chặn.

Kiểm tra SQL/HTTP dùng database UUID riêng của fixture: POS đã thu đủ tại tồn 0/giữ 2 chốt thành công, tồn thành -1, giữ vẫn 2, một payment/một bút toán; bấm chốt lại bị từ chối theo trạng thái Completed mà không ghi trùng. Ma trận cho phép tồn âm từ executor/kho được kiểm tra ở cả tồn cũ và precision 1, gồm trường hợp vẫn chặn khi cả hai tắt.

Bằng chứng chạy: `Logs/pos-delivery-negative-20261009/`. Build dùng artifacts riêng; source chưa được triển khai hoặc khởi động lại trên ứng dụng đang chạy.

Kết quả cuối: build Web/Tests thành công, 0 lỗi/0 cảnh báo; 10 kiểm tra SQL/HTTP đều qua. TRX: `TestResults/pos-delivery-negative-verified.trx` trong thư mục bằng chứng, cùng `build-final.txt` và `verification.json`.
