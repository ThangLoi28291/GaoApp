# Coding Rules cho AI khi sửa GaoApp

## Quy tắc trả lời

Khi người dùng gửi code và yêu cầu sửa:

1. Đọc file hiện tại trước.
2. Nói rõ lỗi nằm ở file nào, hàm nào.
3. Không viết lại toàn bộ nếu chỉ cần sửa một đoạn.
4. Viết code thay thế đủ chạy, không viết kiểu mô tả chung chung.
5. Không tự tạo migration nếu chưa được yêu cầu.
6. Nếu bắt buộc cần migration, phải nói rõ lý do và lệnh cần chạy.
7. Sau mỗi bước lớn ghi: `Làm xong bước này rồi báo PASS`.
8. Trả lời tiếng Việt.

## Quy tắc Clean Architecture

- Controller không query `AppDbContext`.
- Controller không chứa nghiệp vụ lớn.
- Service xử lý nghiệp vụ.
- Repository xử lý query dữ liệu.
- DTO không để lẫn trong Web nếu thuộc nghiệp vụ dùng chung.
- Không phụ thuộc ngược từ Domain sang Application/Infrastructure/Web.

## Quy tắc EF Core / SQL

- Hạn chế `Include` quá sâu trong POS.
- Ưu tiên query chọn đúng DTO khi danh sách lớn.
- Không load toàn bộ bảng để filter trên memory.
- Query nhiều lần trong vòng lặp phải xem xét gom query.
- Khi dùng decimal tiền: dùng precision phù hợp, thường `decimal(18,2)`.
- Khi dùng số lượng quy đổi: dùng precision 18,3 hoặc 18,4 tùy module.

## Quy tắc POS

- Quét barcode phải nhanh.
- Đổi số lượng phải tính lại tổng đúng.
- Không phá voucher/reward/promotion.
- Không tính điểm cho hàng tặng.
- Không cho hàng tặng làm sai doanh thu.
- Không để đơn hoàn thành bị sửa như đơn nháp.
- Mọi thao tác thanh toán phải cập nhật Shift đúng.

## Quy tắc Inventory

- Nhập kho phải cập nhật tồn và giá vốn đúng.
- Xuất kho phải cập nhật tồn, giao dịch kho, giá trị tồn.
- Hoàn/void phải đảo kho đúng.
- Tránh âm kho không kiểm soát.
- Khi giá vốn tạm tính phải có dấu vết để xử lý sau.

## Quy tắc Promotion

- Promotion không được làm sai `Order.GrandTotal`.
- Buy X Get Y nếu tạo dòng tặng riêng thì dòng tặng phải có `UnitPrice = 0`, `LineTotal = 0`, `IsPromotionGift = true`.
- Khi đổi số lượng dòng chính phải tính lại dòng tặng.
- Khi xóa dòng chính phải xóa/cập nhật dòng tặng liên quan.
- Không cộng reward cho dòng tặng.
- Không trừ kho sai đơn vị quy đổi.

## Quy tắc Reward/Voucher

- Voucher phải thuộc đúng khách hàng.
- Voucher đã Used/Cancelled/Expired/Locked không được dùng.
- Đổi khách hàng trên giỏ phải xử lý voucher đã áp dụng.
- Không cho đơn âm tiền.
- Void/refund phải hoàn/đảo ledger hợp lý.

## Quy tắc bảo mật

- Không hard-code mật khẩu, secret, connection string vào tài liệu hoặc code mẫu.
- Không bỏ qua permission khi thêm controller/action admin.
- Không bỏ qua tenant guard.
