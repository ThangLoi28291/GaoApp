# Đặt cọc khách hàng

## Phạm vi
- Menu Khách hàng / Đặt cọc khách hàng, đường dẫn `/admin/customer-deposit`.
- Mở trang sẽ thấy danh sách phiếu của tất cả khách trong cửa hàng, bao gồm phiếu đã hết số dư. Danh sách phân trang 25 phiếu, tìm theo mã PCOC-/DC-, tên, điện thoại hoặc nội dung; lọc còn dư / hết số dư. Tổng quan tính trên toàn bộ kết quả của bộ lọc, không chỉ trang đang xem.
- Chọn khách vẫn có nhận cọc, phiếu còn dư, sổ giao dịch và hoàn cọc; tab **Tất cả phiếu** xem cả phiếu đã hết số dư của khách. Nút **Danh sách tất cả phiếu** quay về danh sách tổng.
- Nút **In phiếu cọc** có tại danh sách, phiếu còn dư, giao dịch nhận cọc và thông báo sau khi nhận thành công. Bản in `/admin/customer-deposit/{depositId}/print` dùng quyền `customer.deposit.view`, giới hạn cửa hàng, có thông tin cửa hàng/khách, nội dung, ngày hẹn giao, tiền nhận ban đầu, phương thức hiện tại, tham chiếu, người thu/ca và chữ ký. Số dư hiện tại ghi riêng; in lại không thay đổi số tiền nhận khi phiếu đã dùng/hoàn. Mẫu in chỉ dùng khổ 80mm, lề 4mm và chiều dài theo nội dung phiếu; có nút mở hộp thoại in hoặc lưu PDF của trình duyệt.
- Phiếu cọc gắn khách, nội dung hàng đặt, ngày dự kiến giao; chưa phải một module đơn đặt hàng có danh sách SKU và giữ hàng.
- Nhận cọc tiền mặt vào CashIn của ca hiện tại. Chuyển khoản lưu tài khoản mặc định đang hoạt động với ConfirmMode=Manual và mã giao dịch nhân viên đã đối soát. Không gọi ACB, tạo QR tự động hay gửi lệnh chuyển tiền.
- Nếu ngân hàng mặc định đang dùng Callback/Polling, yêu cầu cấu hình ngân hàng mặc định thủ công; không tự chọn một tài khoản khác.
- POS kế thừa khách đã chọn. Mỗi đơn chọn một phiếu cọc, nhập số sử dụng trong số dư và phần còn phải trả. Phiếu có thể dùng nhiều lần, số dư còn lại giữ cho khách.
- Chọn cọc chưa giữ/trừ tiền. Chỉ finalize thành công mới trừ cọc trong cùng transaction xuất kho/chốt đơn. Nếu cọc đã được dùng/hoàn ở nơi khác, chốt bị từ chối để chọn lại.
- Tiền cọc là thanh toán, không phải giảm giá: không làm giảm tổng hàng/tích điểm. Không tạo OrderPayment giả hoặc cộng lại tiền vào ca bán hàng.
- Phần còn thiếu có thể trả tiền mặt/chuyển khoản hoặc ghi công nợ theo quyền hiện có.
- Hủy đơn khôi phục cọc. Trả hàng sau bán cho chọn hoàn tiền thực tế hoặc hoàn vào số dư cọc còn có thể hoàn; một khoản chỉ hoàn một lần. Hoàn vào cọc không phát sinh tiền ra ca.
- Hoàn cọc chưa sử dụng là phiếu chi riêng; cần quyền hoàn cọc, ca mở và số dư đủ. Chuyển khoản là ghi nhận giao dịch đã thực hiện, không tự chuyển tiền.
- Các giao dịch nhận/hoàn có mã idempotency, kiểm tra payload cũ. Khóa giao dịch ngăn dùng/hoàn vượt số dư.
- Sổ cọc lưu nhận, dùng, hoàn, khôi phục do hủy; tham chiếu đơn, ca, nhân viên, ngân hàng. Giao diện hiển thị 500 giao dịch gần nhất. Dư nợ khách độc lập với số dư cọc.
- Khoản nhận cọc có nút **Đổi phương thức nhận cọc** trong sổ cọc và màn hình đối soát. Nhân viên đề nghị đổi tiền mặt/chuyển khoản và ghi lý do; không bắt buộc mã giao dịch. Quản lý duyệt mới cập nhật phương thức và phiếu thu cọc liên quan. Số tiền nhận và số dư cọc được giữ nguyên, kể cả khi cọc đã dùng/hoàn một phần. Ca đã đóng cần đối soát lại. Chi tiết: [POS payment adjustments](development/POS-PAYMENT-ADJUSTMENTS-20261004.md).

## Triển khai
Nâng cấp danh sách tổng và bản in ngày 09/10/2026 sử dụng schema đặt cọc hiện có, không thêm migration hay quyền mới.

Chưa áp dụng lên database bán hàng thật. Migration AddCustomerDeposits và AddDepositReturnRestoration thêm CustomerDeposits, CustomerDepositEntries và Orders.CustomerDepositId/DepositAmount, không đổi dữ liệu thanh toán cũ.

1. Sao lưu và áp dụng migration bằng quy trình Migrator --schema-only của dự án trên đúng môi trường.
2. Chạy --security-seed để cập nhật menu/quyền. Không chạy demo seed.
3. Cấp các quyền customer.deposit.view / receive / use / refund theo trách nhiệm nhân viên.
4. Cấu hình tài khoản mặc định xác nhận thủ công cho thu/chi cọc chuyển khoản.
5. Kiểm tra trên staging trước khi chạy bán hàng thực tế.

## Kiểm thử
CustomerDepositSqlServerTests dùng database tạm để kiểm tra nhận trùng, ngân hàng mặc định, hoàn cọc, chống vượt số dư/khác cửa hàng/thiếu quyền, dùng cọc + công nợ + trả hàng và khôi phục cọc khi void.
Browser probe: `dotnet run --project GaoApp.Tests.Browser/PosOffline.Browser.csproj -- --customer-deposit`, đặt GAOAPP_TEST_SQL_SERVER phù hợp cho database tạm.

Kết quả ngày 19/09/2026: build Web không lỗi; 5 kiểm thử SQL đặt cọc và 15 kiểm thử hồi quy thanh toán/công nợ/giao diện đã qua. Logs: customer-deposit-tests-final.log và customer-deposit-regression.log.

Kết quả nâng cấp ngày 09/10/2026: build thành công; 7 kiểm thử `CustomerDepositSqlServerTests` qua trên database tạm của SQL Server cục bộ. Browser probe qua luồng danh sách tổng, tìm mã phiếu, giao diện điện thoại, in/lưu PDF, nhận QR, chọn/bỏ chọn cọc tại POS, ghi công nợ và trả hàng hoàn vào cọc. Logs: `Logs/customer-deposit-upgrade-build.log`, `Logs/customer-deposit-upgrade-tests-sql.log`, `Logs/customer-deposit-upgrade-browser-sql.log`. Ảnh và PDF kiểm tra: `TestResults/customer-deposit/`.

Điều chỉnh khổ 80mm ngày 09/10/2026: build và kiểm tra trình duyệt riêng cho bản in qua; xác nhận bề rộng nội dung/PDF 80mm, không tràn ngang, chiều dài theo nội dung và nút in hoạt động. Logs: `Logs/customer-deposit-80mm-build.log`, `Logs/customer-deposit-80mm-browser.log`. Mẫu kiểm tra: `TestResults/customer-deposit/receipt-print.png`, `TestResults/customer-deposit/receipt.pdf`.
