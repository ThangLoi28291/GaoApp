# Công nợ khách hàng tại POS

## Phạm vi

- Khách được chọn duy nhất ở POS; thanh toán kế thừa khách, giá và tích điểm của đơn.
- Hiện Công nợ khi khách đang hoạt động có `HaveDebt` và nhân viên có quyền `customer.debt.sell`.
- POS chỉ ghi nợ và ghi chú, không yêu cầu ngày hẹn trả. Đơn mới không hẹn ngày có CreditDueDate = null; các hạn trả cũ được giữ nguyên. Ngưỡng nợ và số ngày nợ dành cho nâng cấp quản lý công nợ sau này.
- Thu tiền trả ngay qua luồng Tiền mặt/Chuyển khoản hiện có, sau đó chốt phần còn thiếu thành công nợ; hỗ trợ nợ toàn bộ.
- Đơn chốt vẫn xuất kho và ghi nhận tích điểm theo các dòng đủ điều kiện; thu nợ sau này không cộng điểm/doanh thu bán hàng lần nữa.
- Không ghi công nợ offline. QR đang chờ cần được hủy/đối soát trước khi chốt nợ.
- `/admin/customer-debt`: danh sách khách, đơn và hạn trả, sổ tăng/giảm nợ, phiếu thu, chi hoàn tiền, Excel đối chiếu.
- Thu nợ bằng tiền mặt/chuyển khoản, chọn một đơn hoặc tự phân bổ theo hạn trả. Chuyển khoản phải chọn tài khoản và mã giao dịch đã đối soát.
- Thu nợ cần ca đang mở thuộc người thu tại quầy hiện tại. Tiền mặt vào CashIn của ca thu; không sửa ca bán cũ. Chuyển khoản được đối chiếu qua phiếu thu và tài khoản.
- Trả hàng ưu tiên giảm nợ; chỉ hoàn phần vượt số nợ còn lại. UI tự tính tiền hoàn, server kiểm tra lại. Chi hoàn giữ chứng từ trả hàng và ca hoàn hiện có.
- Hủy đơn nợ chưa thu nợ tạo phát sinh đảo nợ. Đơn đã thu nợ dùng trả hàng/hoàn tiền thay vì hủy ngược ca cũ.

## Tính toàn vẹn

`CustomerReceivableEntries` lưu sổ tăng/giảm nợ theo khách/đơn/chứng từ; `CustomerDebtReceipts` lưu lần thu và mã chống trùng. Không có API sửa/xóa số dư hoặc phiếu thu. Thu nợ khóa khách, ca và đơn trong transaction; chốt/trả/hủy khóa đơn. Request cũ gửi lại không ghi thêm tiền. Giới hạn thu được kiểm tra sau khi khóa. Bộ lọc cửa hàng và các kiểm tra StoreId áp dụng trước ghi sổ.

OrderPayment của lần thu nợ có `IsDebtCollection` để loại khỏi thống kê tiền bán của ca gốc. Công nợ không phải PaymentMethod và không tạo khoản thanh toán giả.

## Đưa vào sử dụng

1. Build Web và Migrator từ cùng mã nguồn. Migration mới: `20260919095814_AddCustomerReceivables` (hai bảng và các cột mở rộng, không chuyển đơn nháp cũ thành công nợ).
2. Dùng cấu hình môi trường đích đã quản lý, chạy Migrator `--schema-only`, sau đó `--security-seed` để cập nhật quyền/menu. Không bật demo seed.
3. Cấp `customer.debt.view`, `customer.debt.sell`, `customer.debt.collect` cho vai trò phù hợp; chốt nợ còn cần quyền chốt đơn POS.
4. Bật Được phép công nợ trong hồ sơ khách được duyệt và kiểm tra một đơn mẫu trên môi trường nghiệm thu trước khi mở rộng.

Chỉ thử migration và giao dịch trên database thử riêng trong quá trình phát triển; không tự cập nhật database bán hàng đang chạy. Không rollback xóa bảng khi đã phát sinh công nợ thật; giữ dữ liệu và sửa tiến về trước.

## Kiểm thử

- `CustomerReceivableSqlServerTests`: chốt nợ toàn bộ/một phần, điều kiện khách/quyền, thu ca sau, retry đồng thời, chống thu vượt, khác cửa hàng, chuyển khoản, trả hàng giảm nợ và hoàn tiền.
- Hồi quy `PaymentCollectionSqlServerTests`, `PosCheckoutPaymentUiContractTests`.
- Browser runner `--customer-debt`: ẩn nút khi chưa chọn khách, kế thừa khách POS, chốt nợ, thu tiền, ảnh desktop/mobile bằng cửa hàng thử riêng.

Hạn mức theo khách, nhắc nợ tự động, nhập dư đầu kỳ và điều chỉnh/xóa nợ có phê duyệt chưa nằm trong đợt cơ bản này. Không có tự động tạo giao dịch ngân hàng; chuyển khoản thu nợ là xác nhận tiền thực nhận đã được đối soát.

## Quản lý và báo cáo thu nợ

- Từ danh sách khách chọn **Quản lý công nợ**, hoặc từ hồ sơ khách chọn **Xem công nợ / thu nợ**. Quyền truy cập là `customer.debt.view`; ghi nhận thu tiền vẫn cần `customer.debt.collect` và ca mở hợp lệ.
- Sổ công nợ lọc theo tên, mã, điện thoại và trạng thái còn nợ / quá hạn / hết nợ; phân trang 50 khách. Các tổng số dư tính trên toàn bộ kết quả theo bộ lọc, không bị giới hạn ở trang đang xem.
- **Báo cáo thu công nợ** tại `/admin/customer-debt/collections`: mặc định từ đầu tháng đến hôm nay, lọc theo khách, tham chiếu ngân hàng, phương thức, người thu và mã ca. Ngày lọc và thời gian hiển thị dùng UTC+7, bao gồm cả ngày kết thúc.
- Báo cáo tổng tiền mặt, chuyển khoản, số phiếu và số khách đã trả nợ. Tổng hợp toàn bộ kết quả; bảng chi tiết phân trang 50 phiếu. Chỉ tính phiếu thu nợ thực nhận, không gộp giảm nợ do trả hàng, hủy đơn hoặc chi hoàn tiền.
- Bấm mã `PTCN-…` để xem người thu, ca thu, tài khoản, tham chiếu, ghi chú và số tiền phân bổ vào từng đơn; có nút **In phiếu thu**.
- **Xuất Excel** giữ nguyên bộ lọc và xuất tất cả các trang. Giới hạn 50.000 phiếu mỗi lần; vượt giới hạn sẽ yêu cầu thu hẹp bộ lọc, không âm thầm cắt dữ liệu.
- Nâng cấp này dùng bảng hiện có, không cần migration mới. Phân quyền và giới hạn cửa hàng được áp dụng cho cả trang, chi tiết phiếu và xuất Excel.
