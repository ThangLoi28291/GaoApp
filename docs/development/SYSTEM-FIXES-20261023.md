# Sửa lỗi và kiểm thử lại GaoApp ngày 23/10/2026

Phạm vi là xử lý các lỗi phát hiện trong [lượt kiểm thử toàn hệ thống](SYSTEM-VERIFICATION-20261023.md), sửa dữ liệu đơn vị theo xác nhận của người dùng và chạy hồi quy. Các kết nối ngân hàng, hóa đơn điện tử, AI và máy in được mô phỏng theo yêu cầu. Không triển khai hoặc khởi động lại IIS và dịch vụ thật.

**Đã hoàn tất sửa lỗi và hồi quy nội bộ: backend 4.749/4.749, JavaScript 673/673 và trình duyệt 47/47 đạt. Không còn ca thất bại hoặc bỏ qua trong kết quả đã hợp nhất.**

## Các vấn đề và cách xử lý

| Vấn đề | Cách xử lý đã thực hiện |
|---|---|
| Ghi nhận tiền nhưng chốt đơn sau đó thất bại | Gộp thu tiền mới và chốt đơn trong cùng giao dịch ở service. Chốt thất bại thì rollback khoản thu trong yêu cầu đó. Giữ đường xác nhận ngân hàng và cơ chế thực hiện tác vụ sau commit của giao dịch ngoài. |
| Đổi đơn vị cơ sở vướng khóa bất biến của EF | Đọc quy đổi không tracking, cập nhật khóa bằng câu lệnh có StoreId và RowVersion trong giao dịch. Giữ ID quy đổi; chặn đổi khi đã có lịch sử, gồm cả đơn giao. |
| Màn hình khách hàng qua SignalR thiếu tenant | Lấy StoreId từ phiên đã xác thực và kiểm tra thu hồi; đặt phạm vi tenant cho mỗi lần gọi rồi xóa trong finally. |
| Control tên method hoặc action làm hỏng xử lý form | Đọc thuộc tính HTML qua getAttribute; bổ sung kiểm thử riêng cho form thanh toán và đăng xuất. |
| Không in lại được hóa đơn khi máy POS gốc đã xóa mềm | Thêm truy vấn chỉ đọc cho hóa đơn. Giữ bộ lọc đơn hàng; chỉ đọc lại tên/mã máy lịch sử với điều kiện StoreId và TerminalId của chính đơn đó. |
| Quét hàng làm nhảy vị trí cuộn giỏ | Khôi phục vị trí sau khi bố cục hoàn tất; không ghi đè khi người dùng đã cuộn hoặc có lần render mới. |
| Duyệt tên phiếu dùng phiên bản cũ khi giá đang tự lưu | Chờ lưu giá nháp trước khi đọc phiếu, giữ khóa chỉnh giá trong thời gian mở hộp thoại và nhả khi đóng. Lỗi lưu giá chặn thao tác và hiển thị nguyên nhân. |
| Link XML trên phiếu đã duyệt không mở phần đối chiếu | Mở đúng phần hóa đơn theo deep link kể cả khi trang không còn bàn làm việc duyệt giá. |
| Token chuẩn bị mã vạch thay đổi dù danh mục giữ nguyên | Sắp xếp cố định các quy đổi trước khi lập kế hoạch xác nhận. Kiểm thử thêm cùng tập sản phẩm theo thứ tự khác vẫn có cùng token; giữ kiểm tra từ chối khi dữ liệu thực sự thay đổi. |
| Replay thao tác soạn hàng không kiểm tra lại nguồn đã ngừng hoạt động | Kiểm tra lại kho và chủ thể nguồn trước khi trả xác nhận replay. |
| Downgrade migration giá vốn làm mất default lịch sử | Khôi phục hai default về 0 sau AlterColumn, tránh câu UPDATE backfill bị trigger chống sửa lịch sử chặn. Không thay Up và không thêm migration. |
| Nhánh hủy worker audit có catch rỗng | Kết thúc vòng xử lý khi nhận cancellation; giữ luồng lỗi thông thường. |
| Công cụ schema bị chặn bởi guard tenant | Dùng TenantContext thật với scope bảo trì ở CLI tin cậy. Giữ nguyên interceptor của ứng dụng; --check và SchemaInspect chỉ gọi đường đọc metadata. |
| Thiếu script khôi phục cấu hình Assistant trên IIS | Bổ sung kiểm tra đường dẫn/endpoint/pool, ShouldProcess, ghi idempotent, sao lưu và rollback. Chỉ kiểm thử bằng IIS giả lập. |
| Script delta danh mục quyền thiếu quyền Assistant | Bổ sung sáu mã vào danh mục. Không cấp quyền, tạo vai trò hoặc menu; chưa chạy script này trên GaoAppDb. |

## Sửa bộ kiểm thử

Đã cập nhật số lượng và thứ tự migration tới bản 80; fixture downgrade chuyển audit đang chờ sang AuditLogs bằng processor thật trước khi thử lùi các tính năng. Các kiểm thử cố ý xác minh chặn mất audit vẫn giữ nguyên.

Fixture khuyến mãi phản ánh đúng số lượng hàng mua và hàng tặng, phân bổ giảm vào dòng và dữ liệu nhập kho thật cho giá vốn tạm. Không thay chính sách giá/khuyến mãi hoặc lấy giá danh mục thay nguồn giá vốn kho.

Các bài kiểm thử giao diện dùng nonce thật, mở các phần thu gọn bằng điều khiển của trang, gửi mật khẩu/lý do xác minh khi đổi quyền và chờ hoàn tất cập nhật trước thao tác tiếp theo. Không nới CSP hoặc quyền truy cập. Ca thử số lượng cực đại dùng SKU riêng để không cộng vượt giới hạn tồn giữ bởi các đơn khác.

JavaScript kiểm tra luồng bàn giao hiện hành, CSRF, quyền, phiên bản, nội dung retry giữ nguyên từng byte và xử lý mất phản hồi. Mock Assistant có Headers như Fetch API thật. So sánh tọa độ đồ họa dùng sai số 1e-9; các phép kiểm tra tiền và lượng vẫn giữ độ chính xác nghiệp vụ.

Ca HTTP hạn mức điện thoại dùng cửa sổ test 30 giây thay vì một giây để thời gian lưu chứng cứ SQL không làm hết hạn trước yêu cầu kiểm tra 429. Vẫn kiểm tra xác thực trước parse JSON, Retry-After, heartbeat, reset hạn mức và đúng một chứng cứ khi thử lại. Truy vấn sổ kho lớn bị timeout khi nhiều host test cùng tải SQL Express đã đạt khi chạy lại riêng, giữ nguyên bộ lọc, phân trang, tenant và các ngưỡng kiểm tra.

## Dữ liệu GaoAppDb

Người dùng xác nhận đơn vị đúng của mặt hàng **bánh kẹo mới, ID 551768, StoreId 1 là Bịch**. Đã lưu bản ghi trước sửa và cập nhật BaseUnitId từ 1 thành 2 bằng giao dịch có điều kiện RowVersion, kèm thời điểm cập nhật. Giữ nguyên quy đổi 70577, hệ số 1 và các dữ liệu khác. Trước sửa mặt hàng chưa có dòng bán hoặc giao dịch kho.

Kiểm tra sau sửa: không còn lệch đơn vị cơ sở giữa sản phẩm và quy đổi gốc. Công cụ kiểm tra schema trả MatchingAppliedPrefix: 79 migration đã áp dụng, một migration đang chờ, không có bản ghi cấu trúc thừa hoặc thiếu. Không nâng schema, thay tài khoản, quyền hay menu trên GaoAppDb.

## Kiểm thử và bằng chứng

Bằng chứng nằm trong `.artifacts/system-fixes-20261023/`. `final-results.json` tổng hợp tiến độ và kết quả; TRX lưu từng lần thực thi. Các shard backend phân vùng theo class; giữ từng dòng kết quả, kể cả khi tên hiển thị của các dữ liệu theory bị rút gọn giống nhau.

Kết quả được hợp nhất theo cả phương thức kiểm thử khi chạy lại; không cộng thêm số lần retry vào tổng ca. Hai bài dữ liệu tài khoản chuẩn có cờ opt-in riêng được chạy với `GAOAPP_SECURITY_STANDARD_SOURCE=GaoAppDb` theo quyền sao chép bảy bảng đã được người dùng cấp. Mọi thay đổi tài khoản, phân quyền và menu của các bài này chỉ diễn ra trên CSDL GUID được fixture tạo và dọn tự động.

| Phạm vi | Kết quả hiện có |
|---|---|
| Backend hồi quy | 4.749/4.749 đạt; đủ 8 phân vùng và toàn bộ discovery, không còn ca bỏ qua sau các lượt xác minh riêng |
| JavaScript UI | 596/596 đạt, không bỏ ca |
| JavaScript bổ sung | 77/77 đạt, không bỏ ca |
| Trình duyệt | 47/47 kịch bản đạt; các ca cần sửa fixture được chạy lại trước khi hợp nhất kết quả |
| Tenant trên build cuối | 11/11 đạt |
| SQL in tem và hạn mức trên build cuối | 13/13 đạt, gồm token cho tập sản phẩm đảo thứ tự và thử lại thông báo ngân hàng không tạo trùng |
| Dữ liệu tài khoản chuẩn | 2/2 đạt trên bản sao bảy bảng từ GaoAppDb; không cộng lại vào tổng backend |
| PowerShell | 133 kiểm tra runtime, deployment safety/tools, launcher, Assistant paths và IIS giả lập đạt; 93 script parse không lỗi |
| Công cụ schema | Hai công cụ build không lỗi/cảnh báo; đọc GaoAppDb và --check đạt |
| Đóng gói | Đủ bốn thành phần; kiểm tra toàn vẹn, runtime portable và client tự chứa đạt; client chỉ gọi /health, từ chối thiếu pairing và origin lạ |

Gói local cuối: `publish/releases/20261023-092543-2bb411b7`. Gói `20261023-084205-2375e84a` được tạo trước lần sửa token mã vạch và đã được thay bằng gói cuối này. Các kiểm tra host dùng runtime portable 10.0.13. Runtime toàn máy hiện có vẫn là 10.0.12; cần cập nhật Hosting Bundle/runtime phù hợp trong bước triển khai thực tế. Chưa kích hoạt gói mới hoặc chạy migration 80 trên dữ liệu thật. Lượt trước đã kiểm tra nâng 79 lên 80 trên bản sao đầy đủ, giữ nguyên số dòng và checksum của 22 bảng.

Kiểm kê cuối xác nhận các CSDL GUID do lượt kiểm thử tạo đã được fixture dọn hết; bảy CSDL GaoApp có từ trước vẫn ONLINE. Kiểm tra lại bảng Products trên GaoAppDb không báo lỗi ràng buộc; đơn vị mặt hàng 551768 là Bịch và số lệch đơn vị cơ sở bằng 0. Bản ghi trước sửa sản phẩm được giữ riêng trong artifacts để đối chiếu. SHA-256 các tệp trong danh mục source ban đầu và các tệp bổ sung được lưu ở `source-changes-final.json`.
