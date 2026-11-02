# Nghiệm thu B1 — Tranh chấp sửa và xác nhận chuyển kho

**Kết quả: đã xử lý B1 trong source và kiểm thử đạt. Chưa publish hoặc triển khai lên môi trường đang chạy.**

Build Release: **0 lỗi, 0 cảnh báo**. Hồi quy liên quan: **264/264 ca đạt**, gồm **24 ca mới cho B1**, không có ca bỏ qua. Đây là lượt nghiệm thu B1 và các phần dùng chung bị ảnh hưởng, không phải chạy lại toàn bộ hệ thống.

## Thay đổi

- Cả bảy thao tác sửa header, thêm/sửa/xóa dòng, gửi duyệt, từ chối và xác nhận đều bắt đầu giao dịch và lấy khóa trên cùng header trước khi đọc trạng thái phục vụ thay đổi. Khóa SQL `UPDLOCK, HOLDLOCK, ROWLOCK` có điều kiện cửa hàng, ID và chưa xóa. Các tham số được truyền qua `FromSqlInterpolated`.
- Với API nhận ID dòng, chỉ tra ID phiếu trước; sau khi lấy khóa phải đọc lại dòng và trạng thái phiếu, kiểm tra dòng vẫn thuộc đúng phiếu. Không dùng trạng thái đã đọc trước khóa để cho phép sửa.
- Kiểm tra RowVersion có sẵn của header/dòng đã được EF theo dõi; dữ liệu cũ hoặc thay đổi chưa lưu bị từ chối, không bị âm thầm bỏ qua. Sau đó đọc lại graph trong khóa. Mọi thay đổi dòng cũng cập nhật header để tăng RowVersion của toàn phiếu.
- Xác nhận giữ khóa từ lúc đọc phiếu đến hết ghi xuất/nhập, cập nhật trạng thái và commit. Khi lỗi hoặc hủy yêu cầu, rollback dùng token không bị hủy để giải phóng giao dịch. Không nhận dùng chung hoặc tự commit một giao dịch ngoài do caller sở hữu.
- Giữ nguyên nghiệp vụ cho sửa Draft/Rejected/PendingApproval; Confirmed bị khóa. Giữ thứ tự xuất trước nhập, khóa số dư, cơ chế chống ghi trùng và nhánh bảo toàn chứng cứ giá vốn. Không đổi request API, phân quyền, CSRF hoặc menu; không thêm migration cho B1.

Các tệp ứng dụng thay đổi:

- `GaoApp.Application/Interfaces/Repositories/Inventory/IStockTransferRepository.cs`
- `GaoApp.Application/Services/Inventory/StockTransferService.cs`
- `GaoApp.Infrastructure/Repositories/Inventory/StockTransferRepository.cs`

## Kết quả kiểm chứng

Hai tài khoản HTTP có quyền sửa và xác nhận riêng biệt, Web thật, SQL Server thật; khóa trên dữ liệu test dùng để tạo chính xác thứ tự tranh chấp. Không thay handler nghiệp vụ bằng mock.

| Nhóm kiểm thử B1 mới | Số ca | Kết quả |
| --- | ---: | --- |
| Confirm giữ khóa trước; cạnh tranh với cả bảy thao tác, rồi thử xác nhận lại | 7 | Confirm thành công, thao tác đến sau bị từ chối; mỗi dòng chỉ có một cặp xuất/nhập |
| Tái hiện lịch thực thi của lỗi cũ: thêm sản phẩm thứ hai trước Confirm | 1 | Confirm phải chờ thêm dòng; sau đó xác nhận hai dòng và đủ bốn bút toán |
| Sửa dòng/xóa dòng/header/từ chối hoàn tất trước xác nhận | 4 | Dùng nội dung mới; phiếu Rejected phải gửi lại trước xác nhận |
| Tám yêu cầu thêm đồng thời cùng sản phẩm/đơn vị | 1 | Cộng đủ số lượng, không mất cập nhật hoặc trùng số dòng |
| Dòng sau thiếu tồn, sau khi dòng đầu đã bắt đầu posting | 1 | Hoàn tác toàn bộ số dư và bút toán; yêu cầu mới vẫn sửa/xác nhận được |
| Dòng thay đổi làm tăng RowVersion; graph cũ | 1 | Chặn graph cũ bằng lỗi concurrency; không xóa nhầm dữ liệu mới |
| Hủy yêu cầu khi đang chờ khóa | 1 | Không sửa phiếu, giao dịch được giải phóng; yêu cầu mới tiếp tục được |
| Hai phiếu độc lập | 1 | Khóa phiếu thứ nhất không chặn sửa phiếu thứ hai |
| Khác cửa hàng qua ID phiếu/ID dòng, thiếu quyền, thiếu CSRF, sai đơn vị | 5 | Bị từ chối, không thay chi tiết phiếu hoặc ghi kho |
| Xác nhận qua service thật với tồn đã được nâng cấp chính sách giá vốn, hai mức giá | 2 | Bảo toàn lượng, tiền và liên kết chứng cứ nguồn; xác nhận lại không ghi trùng |

240 ca hồi quy còn lại kiểm tra các luồng posting POS và ngoài POS, khóa số dư, chống trùng, giá vốn, reservation, tenant, hoạt động cửa hàng và hợp đồng giao diện chuyển kho. Toàn bộ 264 ca chạy trên cùng bản build cuối cùng, đạt trong khoảng 6 phút 7 giây.

Kết quả lỗi nghiệp vụ giữ hợp đồng API hiện có: sửa phiếu đã xác nhận hoặc xác nhận lại trả HTTP 400 kèm thông báo. Xung đột RowVersion tại repository ném `DbUpdateConcurrencyException`, được middleware hiện có ánh xạ HTTP 409. Không biến xác nhận lặp thành một lần ghi kho mới.

## Dữ liệu và triển khai

Đã chạy SELECT thống kê trên GaoAppDb: **0 phiếu chuyển kho đã xác nhận**, do đó chưa có dữ liệu lịch sử để kết luận đối soát B1 trên CSDL này. Không ghi GaoAppDb; số migration vẫn **79**. Kiểm thử nghiệp vụ dùng CSDL GUID riêng. Sau kiểm thử chỉ còn đúng **7 CSDL GaoApp có từ trước**, đều ONLINE; không còn CSDL test mới.

Đối chiếu hash với 3.726 tệp của lần nghiệm thu trước xác nhận chỉ ba tệp ứng dụng nêu trên thay đổi trong tập đó. Bổ sung hai tệp kiểm thử và báo cáo này. Không phát sinh giao dịch ngân hàng, hóa đơn điện tử hoặc lệnh in thật.

B1 đã hoàn thành ở mức source và kiểm thử. Khi triển khai, cần đưa mọi instance ứng dụng sang bản sửa và để các request của bản cũ kết thúc trước khi coi môi trường vận hành đã được khắc phục. Không tự triển khai trong lượt này. B2–B4 tiếp tục là các mục riêng của báo cáo đánh giá.

## Bằng chứng và chạy lại

Thư mục `.artifacts/b1-transfer-fix-20261023/` chứa:

- `build-verified.log`: build cuối 0 lỗi, 0 cảnh báo; `restore-audit.log`: làm mới NuGet thành công, giữ nguyên phiên bản phụ thuộc.
- `results/b1-regression.trx`, `regression.log`: kết quả cuối 264/264.
- `run-regression.ps1`, `regression-filter.txt`: lệnh và phạm vi chạy lại trên SQL Server test.
- `historical-transfer-check.sql`, `.txt`: SELECT thống kê GaoAppDb.
- `databases-after.txt`, `source-scope-check.json`, `verification.json`: dọn test, phạm vi thay đổi và hash source/binary.
- `before/`, các tệp `.diff`: bản trước khi sửa và diff riêng cho B1, tách khỏi thay đổi đã có trong workspace.

Các log attempt được giữ lại để truy vết; attempt đầu của bộ test mới có lỗi ép kiểu mã phiên SQL từ smallint sang int. Lỗi nằm ở mã điều phối kiểm thử, đã sửa và chạy lại toàn bộ phạm vi nêu trên. Kết luận nghiệm thu dùng TRX cuối.
