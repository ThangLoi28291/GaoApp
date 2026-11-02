# Các rủi ro bổ sung sau lượt rà soát — 23/10/2026

**Có thêm ba vấn đề đã tái hiện và một rủi ro có điều kiện cần xử lý.** Lượt này đi sâu các phần khác theo yêu cầu; không đưa lại các mục đã được người dùng yêu cầu bỏ qua vào danh sách đề xuất.

Không sửa code ứng dụng, phân quyền/menu, migration hoặc dữ liệu vận hành. Các phép thử dùng Web và SQL Server thật với tài khoản/dữ liệu tổng hợp trong CSDL GUID do fixture tự tạo, tự dọn; không gọi ngân hàng, phát hành hóa đơn, AI hoặc máy in thật. Đây là kiểm chứng bổ sung, không phải lần chạy lại toàn bộ bộ hồi quy.

## B1 — Cao: Có thể thêm dòng sau khi phiếu chuyển kho đã xác nhận trong lúc hai thao tác chạy đồng thời

**Cập nhật sau xử lý:** B1 đã sửa trong source, build 0 lỗi/0 cảnh báo; 264/264 ca hồi quy liên quan đạt, gồm 24 ca mới. Chưa triển khai. Xem [báo cáo nghiệm thu B1](SECURITY-B1-STOCK-TRANSFER-FIX-20261023.md). Phần dưới giữ bằng chứng của thời điểm phát hiện trước khi sửa.

**Điều kiện:** có một tài khoản được phép sửa chuyển kho và một thao tác xác nhận hợp lệ diễn ra đồng thời. Có thể xảy ra do người dùng thao tác bình thường trên hai máy; không đòi hỏi người ngoài vượt đăng nhập.

`StockTransferService.AddLineAsync` đọc phiếu và kiểm tra `EnsureEditable(document.Status)` ở đầu hàm, sau đó đọc sản phẩm/đơn vị và lưu dòng. Việc thêm dòng không khóa và kiểm tra lại trạng thái phiếu trong cùng giao dịch. `ConfirmAsync` đọc chi tiết/trạng thái trước khi bắt đầu giao dịch ghi kho. RowVersion của phiếu không được kiểm tra khi chỉ INSERT thêm dòng con.

Nguồn: `GaoApp.Application/Services/Inventory/StockTransferService.cs:210,215,370,385`; `GaoApp.Infrastructure/Repositories/Inventory/StockTransferRepository.cs:142`. API nằm tại `/admin/api/stock-transfers`.

**Đã tái hiện qua hai tài khoản HTTP thật:**

1. Phiếu có một dòng, trạng thái chờ xác nhận.
2. Tài khoản chỉ có `inventory.stocktransfer.update` gửi thêm một sản phẩm khác. SELECT giữ khóa trên đúng sản phẩm test đó để yêu cầu dừng ở bước đọc sản phẩm sau khi đã đọc trạng thái phiếu. Khóa chỉ dùng để tạo lịch thực thi xác định; không thay đổi ràng buộc hoặc code ứng dụng.
3. Tài khoản quản trị xác nhận phiếu qua HTTP thật; xuất/nhập kho thành công cho dòng ban đầu.
4. Nhả khóa; yêu cầu thêm dòng đang dở tiếp tục, trả **HTTP 200**.
5. Kết quả: phiếu vẫn **Confirmed**, có **hai dòng**; sản phẩm ở dòng mới có **0 giao dịch kho**. Gửi một yêu cầu thêm dòng mới sau thời điểm đó bị **HTTP 400**, xác nhận nhánh kiểm tra thông thường vẫn chặn đúng khi đọc trạng thái mới.

**Ảnh hưởng:** chi tiết phiếu đã chốt có thể không khớp những hàng thực sự đã xuất/nhập. Đối soát, báo cáo hoặc thao tác tiếp theo dựa trên phiếu có thể hiểu sai. Phép thử chưa chứng minh tiền hoặc tồn kho thực tế đã bị mất; sai lệch được xác nhận là giữa nội dung phiếu và bút toán kho đã ghi.

**Phương án A đề xuất:** mọi thao tác sửa header/thêm/sửa/xóa dòng, gửi duyệt, từ chối và xác nhận phải phối hợp khóa trên cùng phiếu. Đọc lại trạng thái và kiểm tra phiên bản trong giao dịch chứa thay đổi; phiếu đã xác nhận phải chặn cả yêu cầu bắt đầu từ trước. Giữ cơ chế posting và chống trùng hiện có.

**Nghiệm thu:** tranh chấp Add/Update/Delete/Header với Confirm chỉ cho một trạng thái hợp lệ; kết quả xung đột rõ ràng; mọi dòng trên phiếu xác nhận phải khớp bằng chứng xuất/nhập, không phát sinh ghi kho hai lần. Tái thử với hai tài khoản có quyền riêng, nhiều dòng và mất phản hồi.

## B2 — Trung bình, có thể cao tùy vai trò: Giới hạn xem giá mua không nhất quán với thư viện hóa đơn XML

**Cập nhật sau xử lý:** đã triển khai phương án A trong source; build 0 lỗi/0 cảnh báo, 191/191 ca liên quan đạt, gồm 84 ca B2 mới và 24 ca B1. Chưa triển khai lên bản đang chạy. Xem [báo cáo nghiệm thu B2](SECURITY-B2-INVOICE-COST-ACCESS-20261023.md). Phần dưới giữ bằng chứng trước khi sửa.

`StockDocumentsController.GetDetail` che các giá trị thương mại khi người đọc không có quyền xem giá hoặc quyền duyệt phù hợp. Nhưng `InputInvoiceLibraryController` chỉ yêu cầu `inventory.stockdocument.view` cho list/detail/preview và tải PDF/XML; service trả nguyên tổng tiền, tiền trước thuế, thuế và nội dung chứng từ.

Nguồn: `GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs:238`; `GaoApp.Web/Areas/Admin/Controllers/InputInvoiceLibraryController.cs:11,23,26,31,38`; `GaoApp.Infrastructure/Services/Invoices/InputInvoiceCatalogService.cs:54,86`.

**Đã tái hiện:** tài khoản chỉ có `inventory.stockdocument.view`, không có quyền xem giá mua/duyệt, đọc được tổng **108.000**, tiền trước thuế **100.000** và tải XML **HTTP 200** chứa `<DGia>100000</DGia>` trong dữ liệu giả lập. Tài khoản cửa hàng khác nhận **404**: vấn đề xác nhận ở đây là ranh giới quyền thương mại trong cùng cửa hàng.

**Ảnh hưởng:** nếu giao quyền xem chứng từ cho nhân viên nhưng dự kiến giấu giá mua, họ vẫn có thể lấy thông tin qua thư viện hóa đơn. Thư viện còn chứa hóa đơn chưa gắn phiếu, nên phạm vi dữ liệu có thể rộng hơn một phiếu nhập cụ thể. Cần chốt chính sách nghiệp vụ: quyền xem thư viện có bao gồm toàn bộ giá/thuế hay không; hiện hai luồng không thực thi cùng một giới hạn.

**Phương án A đề xuất:** thống nhất kiểm tra quyền dữ liệu thương mại trên HTML, JSON, preview và tải tệp; người không đủ quyền chỉ nhận metadata được phép. PDF/XML gốc chứa giá phải có quyền phù hợp trước khi tải. Không chỉnh sửa XML đã ký để giả thành tài liệu gốc đã che giá. Giữ kiểm tra cửa hàng; không tự cấp thêm quyền hoặc tạo menu.

**Nghiệm thu:** tài khoản chỉ xem chứng từ không lấy được giá qua bất kỳ đường tải/preview nào theo chính sách đã chốt; tài khoản đủ quyền vẫn tải được bản gốc; cửa hàng khác tiếp tục bị chặn.

## B3 — Trung bình: API log chưa giới hạn cứng số bản ghi trả về

**Cập nhật sau xử lý:** B3 đã hoàn thành trong source theo phương án A: tối đa 100 dòng/trang, kiểm tra đầu vào/offset và hạn mức đọc. Hồi quy 291/291 và kiểm tra bổ sung sau cập nhật phân luồng Assistant 73/73 đạt; chưa triển khai. Các thay đổi Assistant tiếp tục diễn ra riêng được giữ nguyên và giới hạn kết luận được nêu tại [báo cáo nghiệm thu B3](SECURITY-B3-LOG-READ-LIMITS-20261023.md). Phần dưới giữ bằng chứng ở thời điểm trước khi sửa.

`AuditLogQueryDto` nhận `PageSize` không có trần. Repository chỉ thay số không dương bằng 20 rồi truyền thẳng `Take(pageSize)`. Luồng log tích hợp hóa đơn có cùng cách xử lý. Hai controller này cũng chưa nằm trong nhóm giới hạn đọc nặng của `ResourceEndpointPolicy`.

Nguồn: `GaoApp.Application/DTOs/Audit/AuditLogQueryDto.cs:25`; `GaoApp.Infrastructure/Repositories/Audit/AuditLogRepository.cs:40,84`; `GaoApp.Infrastructure/Repositories/Invoices/InvoiceIntegrationLogRepository.cs:58,123`; `GaoApp.Web/Security/ResourceEndpointPolicy.cs:36`.

**Đã tái hiện:** tài khoản chỉ có `system.auditlog.view` gọi `/admin/api/audit-logs?page=1&pageSize=2147483647`, nhận **331 bản ghi** trong một phản hồi trên CSDL test nhỏ. Không có trần 100/200 bản ghi ở đường này. Phép thử không cố làm cạn RAM hoặc dồn tải lên dữ liệu thật; mức ảnh hưởng tải lớn được suy ra từ truy vấn không có trần.

**Ảnh hưởng:** tài khoản có quyền đọc, hoặc phiên của họ bị lấy cắp, có thể yêu cầu tải lượng log lớn và lặp lại để chiếm RAM, CPU, SQL và băng thông. Mức ảnh hưởng tăng theo dữ liệu tích lũy. Phân quyền đọc đúng không ngăn được lạm dụng tài nguyên qua yêu cầu đã được phép.

**Phương án A đề xuất:** đặt trần PageSize ở biên request và tầng đọc dùng chung, kiểm tra page/offset, giới hạn bộ lọc; bổ sung hạn mức đồng thời/tần suất cho đọc log. Nếu cần xuất nhiều dữ liệu, dùng luồng xuất có kiểm soát và phân lô riêng.

**Nghiệm thu:** PageSize cực lớn bị từ chối hoặc giới hạn theo chính sách, không trả quá trần; page gây tràn số không thành lỗi 500; thao tác lọc/phân trang hợp lệ vẫn đúng. Mô phỏng tải trên DB test có nhiều log, xác nhận không làm nghẽn POS.

## B4 — Có điều kiện: Log tích hợp cũ được trả nguyên nội dung, chưa có lớp che bí mật khi đọc

**Cập nhật sau xử lý:** B4 đã hoàn thành trong source theo phương án A: che tại các đường đọc dùng chung, giữ metadata chẩn đoán đã kiểm tra và chặn dò qua tìm kiếm URL/lỗi thô. Hồi quy rộng 533/533 và xác nhận bản cuối 312/312 đạt, gồm 83 ca B4; chưa triển khai. Thống kê chỉ đọc GaoAppDb có 23 log, chưa thấy nhãn credential nghi vấn; không xuất raw log, không sửa lịch sử hoặc tự xoay khóa. Xem [báo cáo nghiệm thu B4](SECURITY-B4-LEGACY-INVOICE-LOG-REDACTION-20261023.md). Phần dưới giữ bằng chứng trước khi sửa.

Các client Viettel hiện có `RedactedRequestSummary`/`RedactedResponseSummary` trước khi ghi log mới. Tuy nhiên, `InvoiceIntegrationLogService.MapDetail` gán thẳng `RequestBody` và `ResponseBody` từ bản ghi vào DTO; giao diện hiển thị nguyên nội dung. `RequestUrl` và thông báo lỗi cũng chưa có lớp lọc bí mật dành cho dữ liệu lịch sử tại biên đọc này.

Nguồn: `GaoApp.Application/Services/Invoices/InvoiceIntegrationLogService.cs:106`; `GaoApp.Web/Areas/Admin/Views/InvoiceIntegrationLog/Detail.cshtml:231,261`; `GaoApp.Infrastructure/Services/Invoices/ViettelClientHelper.cs:116`.

**Đã xác minh cơ chế đọc bằng bản ghi lịch sử giả lập:** tạo log chứa password và access_token thử, đăng nhập bằng tài khoản chỉ có `system.integration.view`, mở trang detail nhận **HTTP 200**, cả hai chuỗi thử hiện trong HTML. Chuỗi thử không phải credential thật. Không đọc/kiểm tra nội dung log trên GaoAppDb; chưa có bằng chứng log vận hành thực sự đang chứa bí mật.

**Ảnh hưởng nếu có log cũ chứa bí mật:** người được cấp quyền đọc chẩn đoán có thể thấy credential/token mà không cần quyền quản lý tích hợp. Nếu dữ liệu lịch sử không chứa bí mật thì điều kiện khai thác này chưa tồn tại; đây là khoảng trống bảo vệ dữ liệu cũ cần phân biệt với một vụ lộ đã xác nhận.

**Phương án A đề xuất:** lọc dữ liệu nhạy cảm tại tầng đọc dùng chung cho mọi UI/API của log, giữ trace, trạng thái và thông tin chẩn đoán cần thiết; xử lý cả dữ liệu không phải JSON, URL và lỗi lịch sử. Đánh giá log cũ bằng thống kê đã che dữ liệu trước; nếu xác nhận credential thật đã xuất hiện thì lập phương án thu hồi/xoay khóa. Không tự xóa/sửa lịch sử để mất dấu vết.

**Nghiệm thu:** bản ghi giả lập chứa secret trong JSON lồng nhau, text/URL/lỗi không làm lộ giá trị khi đọc; log mới tiếp tục được giảm dữ liệu trước ghi; thông tin chẩn đoán và cách ly cửa hàng giữ đúng.

## Bằng chứng và phạm vi kết luận

- Lần thử ban đầu chứng minh B2, sau đó SQL chặn fixture hóa đơn thiếu cờ nguồn. Chỉ sửa fixture sang hóa đơn tổng hợp hợp lệ, giữ nguyên ràng buộc; CSDL lần đó được dọn. Đây không phải lỗi ứng dụng.
- Lần tiếp theo tái hiện bốn điểm, trong đó B1 dùng điều phối lịch thực thi ở service/SQL. Lần cuối B1 được xác nhận lại bằng **hai yêu cầu HTTP thật**, dùng khóa đọc sản phẩm của CSDL test để tạo đúng thời điểm tranh chấp. Kết quả B2–B4 được kiểm tra lại trong cùng lần cuối.
- Bằng chứng: `.artifacts/additional-security-review-20261023/probe-results.json`, `probe-run.log`, các attempt trước và `Probe.cs`/`run-probe.ps1`. Probe dùng assembly đã được kiểm thử ở lượt sửa trước; không rebuild/thay đổi module ứng dụng. DLL probe riêng chỉ ở thư mục output test.
- Không dùng các kết quả trên để khẳng định có kẻ tấn công, dữ liệu thật đã bị đọc trộm hoặc tổn thất tiền/kho đã xảy ra. B1–B3 là hành vi được xác nhận trên dữ liệu tổng hợp; B4 xác nhận thiếu lớp che khi đọc, còn sự tồn tại secret trong log thật chưa được kiểm tra.

**Ưu tiên đề xuất: B1 → B2 → B3 → B4.** Mỗi vấn đề sẽ thảo luận và duyệt phương án trước khi sửa code theo quy trình người dùng yêu cầu.
