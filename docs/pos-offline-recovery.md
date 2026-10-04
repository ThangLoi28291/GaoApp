# Xử lý client POS offline không đồng bộ

## Nguyên nhân đã xác định

File đối soát của client có hàng đợi 353 thao tác. Thao tác đầu tiên cập nhật dòng hàng với `qty=138935001002581`, vượt giới hạn an toàn của POS. Client cũ coi lỗi HTTP từ server như lỗi mất mạng, giữ nguyên thao tác đầu hàng đợi và lặp lại mãi nên các thao tác hợp lệ phía sau không thể chạy.

## Phần đã sửa trong bản phát hành

- POS client từ chối số lượng nhỏ hơn hoặc bằng 0 và lớn hơn 1.000.000 khi thêm hàng hoặc sửa số lượng.
- POS server kiểm tra cùng giới hạn trước khi ghi dữ liệu vào cơ sở dữ liệu.
- Khi mở lại hàng đợi cũ, client nhận diện thao tác số lượng bất thường trong URL, dữ liệu POST và bản ghi đơn cục bộ trước khi gửi lên server.
- Nếu server từ chối một khoản thu không tiền mặt vì số tiền lớn hơn số còn phải thu (`overpay`), banner hiển thị nút **Cô lập khoản thu lỗi**. Khoản thu được chuyển vào `quarantined`, giữ nguyên toàn bộ nội dung để đối soát, còn hàng đợi hợp lệ tiếp tục được gửi.
- Với thao tác số lượng sai, banner hiển thị nút **Cô lập thao tác lỗi** với cùng cơ chế bảo toàn dữ liệu.

## Các bước triển khai và khôi phục client hiện tại

1. Sao lưu file JSON gốc và không chỉnh sửa hoặc xóa IndexedDB của máy client. Cần giữ đúng file `gao-pos-pending-1-1790861593285.json` để đối soát sau này.
2. Build và triển khai bản server có mã sửa lỗi. Sau khi triển khai, mở lại trang POS bằng `Ctrl+F5` để tải JavaScript mới; nếu có cache proxy/CDN thì xóa cache asset hoặc tăng version asset.
3. Đăng nhập đúng cửa hàng, đúng máy POS, đúng nhân viên và đúng ca đã tạo hàng đợi. Với file hiện tại, ngữ cảnh là store `1`, terminal `1`, user `641758`, shift `800746`. Không mở thêm tab POS trên cùng máy trong lúc khôi phục.
4. Vào màn hình POS và chờ banner hiển thị **Có thao tác offline cần đối soát**. Bấm **Lưu bản đối soát** trước khi thay đổi hàng đợi.
5. Bấm **Cô lập thao tác lỗi**. Client chỉ chuyển thao tác đầu tiên có `qty=138935001002581` vào danh sách đối soát; các đơn, khoản thu và thao tác hợp lệ vẫn giữ nguyên.
6. Bấm **Thử đồng bộ**. Chờ đến khi số thao tác chờ giảm về 0 hoặc banner báo một lỗi nghiệp vụ khác. Không đóng trình duyệt, xóa dữ liệu trang hoặc tạo ca mới trong lúc hàng đợi còn pending.
7. Khi đồng bộ xong, kiểm tra các đơn đã hoàn tất, tổng tiền, khoản thanh toán và tồn kho trên server. Đối chiếu riêng thao tác bị cô lập để xác định người dùng đã nhập nhầm số lượng hay cần khôi phục thủ công.
8. Nếu banner báo **ca hoặc nhân viên đã thay đổi**, dừng thao tác và đăng nhập lại đúng ca gốc. Không bấm đồng bộ cưỡng bức khi chưa khớp ngữ cảnh.

## Kết quả mong đợi với file hiện tại

Thao tác lỗi đầu tiên được nhận diện theo URL `/admin/pos/lines/1759520?qty=138935001002581`. Thao tác tiếp theo đặt số lượng `13` sẽ được xử lý sau khi thao tác đầu tiên được cô lập. Số pending phải giảm dần; nếu vẫn đứng yên ở cùng `operationId`, hãy bấm **Lưu bản đối soát**, giữ file xuất ra và chuyển log cùng mã thao tác đó để kiểm tra.

Nếu banner hiện `Phương thức này không cho phép thanh toán dư (overpay)`, hãy bấm **Lưu bản đối soát**, sau đó bấm **Cô lập khoản thu lỗi**. Khoản thu bị server từ chối sẽ được giữ trong `quarantined`; cần đối chiếu số tiền thực nhận và ghi lại khoản thu đúng trên server theo quy trình kế toán trước khi đóng ca.

## Không nên làm

- Không xóa toàn bộ IndexedDB hoặc localStorage để “làm sạch” hàng đợi.
- Không sửa trực tiếp file JSON rồi nhập lại nếu chưa có quy trình đối soát được phê duyệt.
- Không gửi lại thủ công tất cả đơn từ đầu; có thể tạo trùng đơn hoặc trùng khoản thu.
