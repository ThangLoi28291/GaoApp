# Đơn giao: giá POS và tồn âm

Ngày: 08/10/2026. Trạng thái: đã viết mã, đang chờ kiểm thử tự động liên quan và review. Chưa nghiệm thu tại tiệm; không chạy browser, full regression, CI hay triển khai trong bước này.

## Giá và voucher

- Tạo đơn giao giữ nguyên giá POS đã áp dụng: giảm dòng, khuyến mãi, combo/mixed-unit, hàng tặng và voucher. Phân bổ riêng giảm ở đầu đơn theo phần dư lớn nhất; đối chiếu tổng Net đúng GrandTotal. Chênh lệch làm tròn dòng/tổng được phân bổ ổn định, hàng tặng không bị thêm tiền. Tổng đơn phải dương, giống thanh toán POS; dòng hàng tặng giá 0 vẫn được chấp nhận.
- Snapshot kiểm tra cả danh tính, giá trị, phiên bản voucher và các thành phần khuyến mãi. Giỏ thay đổi phải tải lại, không tự bỏ giảm giá để tạo đơn.
- Voucher chuyển sang `ReservedForDelivery`, không ghi Used/UsedAt/UsedOrderId và không nhận tiền hay ghi doanh thu. POS khác không thể dùng voucher đang giữ. Hủy đơn trước bàn giao nhả đúng voucher thuộc giỏ nguồn; khóa/mở khóa quản trị không nhả trạng thái này.
- Khi soạn thiếu/thay hàng, giữ giá đã thỏa thuận của dòng gốc, tính tỷ lệ theo lượng và yêu cầu quản lý duyệt thay đổi. Không tự tính lại điều kiện khuyến mãi hoặc đổi giá theo giá hiện tại.

## Tồn và giá vốn

- Kết quả soạn thực tế vẫn giữ hàng khi tồn ghi nhận không đủ. Bàn giao xuất đúng một lần, cho tồn âm như POS. Cập nhật theo yêu cầu chủ ngày 09/10/2026: các đường xuất bán cho phép tồn âm cũng được xuất khi tồn còn lại thấp hơn lượng giữ giao hàng; các giữ hàng khác còn nguyên, không bị POS nhả hoặc tiêu thụ. Nếu executor và kho đều không cho phép tồn âm, tiếp tục bảo vệ phần hàng đang giữ. Không đổi các kiểm tra nguồn/duyệt/idempotency hoặc chính sách giá vốn.
- Phần có nguồn FIFO dùng giá vốn Final và khoảng debit hiện có. Phần thiếu nguồn có allocation tạm tính thật, dùng giá vốn nhập gần nhất/bình quân theo engine; chưa biết thì ghi Unknown, không dùng giá bán và không giả tạo lô Final giá 0.
- Migration74 mở hình dạng chứng cứ tạm tính, giữ nguyên các migration lịch sử. Fragment lưu bất biến lượng/giá vốn ước tính lúc xuất; sau nhập hàng, các debit xác định giá vốn được đọc riêng. Không sửa fragment cũ để đổi ước tính thành số thực tế.
- Nhận trả lấy đuôi fragment gốc. Phần chưa xác định đảo đúng ước tính và allocation nguồn, không tạo lô Final giả; phần đã xác định dùng đúng khoảng debit thực tế. Nhập hàng sau đó chỉ bù phần còn ngoài kho. Chốt tài chính chưa được phép dùng giá vốn của phần ngoài kho còn chưa xác định.
- Không bổ sung tính năng `NegativeInventoryLog`: POS hiện không có writer cho bảng này. Chứng cứ âm tồn là transaction/valuation/allocation thực tế. Down74 phải từ chối khi có chứng cứ không thể biểu diễn bằng schema cũ.

## Giới hạn và kiểm tra tại tiệm

D07/D08 và ghi nhận tài chính chưa thuộc bước này. Các giới hạn D05 về hoàn/void thông thường trên nguồn budget và dữ liệu bù âm lịch sử 32 so với 24 vẫn cần xử lý tại D08/D11 trước phát hành D12; không sửa lịch sử để che sai lệch.

Người dùng tự nghiệm thu: tạo giỏ có khuyến mãi và hàng tặng; tạo giỏ có voucher rồi thử dùng lại voucher ở quầy khác; hủy đơn chưa giao và thử lại voucher; soạn/bàn giao hàng có tồn 0; nhận trả một phần, nhập lô thật và kiểm tra phần còn ngoài kho; thử lại cùng yêu cầu và kiểm tra không xuất/nhận trả lần hai. Đối chiếu bill với POS và kiểm tra quyền người dùng. Full regression/shop/browser chỉ chạy khi người dùng chủ động nghiệm thu, theo yêu cầu đã chốt.
