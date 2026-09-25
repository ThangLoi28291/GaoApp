# Thanh toán QR ACB theo cửa hàng

## Bản hiện hành — 09/09/2026

Đã bổ sung chọn cửa hàng nhận callback từ URL cũ theo **StoreId**, tại `/admin/acb/callback-routing`. Host `www.gaomart.com.vn` được khai báo trong `AcbCallbackRouting:Hosts`; StoreId do quản trị toàn hệ thống chọn và lưu database. Chỉ POST callback dùng ánh xạ này; khóa ACB, inbox và chốt đơn vẫn theo cửa hàng đích. Đổi subdomain không đổi StoreId đã chọn. Migration mới `20260909062834_AddAcbCallbackStoreRouting`, script riêng `docs/acb-callback-store-routing-upgrade.sql`. Bộ hiện tại đạt 240 .NET và 32 JavaScript; các chi tiết triển khai/test R01–R06 ở [acb-callback-store-routing.md](acb-callback-store-routing.md).

POS hỗ trợ nhiều QR động ACB hoặc QR thủ công trên cùng đơn. Mỗi QR mặc định bằng số còn thiếu, cho nhập một phần; QR thủ công dùng tài khoản mặc định. Tạo mới có ClientRequestId riêng, thử lại cùng mã không tạo lệnh trùng. Ghi nhận từng khoản có liên kết lưu bền, chỉ chốt/in khi tổng đủ. Mở lại qua hai GET `/admin/acb/payments/orders/{orderId}/qrs` và `/admin/acb/payments/orders/{orderId}/qrs/{qrId}` chỉ đọc; QR đã kết thúc mở ở chế độ chỉ xem. Phạm vi theo đúng cửa hàng/đơn/ca/máy.

Migration lưu nguồn xác nhận: `20260909024821_AddAcbConfirmationAudit`; script từ bản nhiều lần thanh toán: `docs/acb-confirmation-audit-upgrade.sql`. Bản trước đó là `20260909015242_AddPosQrInstallmentLinks` với script từ bản danh sách giao dịch `docs/pos-qr-installments-upgrade.sql`. Khi một QR trả đủ phần còn thiếu, các QR ACB chưa dùng phải được tra cứu và hủy an toàn trước khi chốt. QR có tiền về bất thường giữ để đối chiếu. Nghiệm thu tiền thật/callback host và giao diện sau khởi động lại vẫn cần người dùng thực hiện. Chi tiết trong `docs/acb-acceptance-tests.md`.

Mỗi AcbQrSession lưu ConfirmationSource, ConfirmedAtUtc, ConfirmedByUserId và ConfirmationCallbackReceiptId khi lần tra cứu đầu tiên xác minh hợp lệ và chuyển sang Received dưới khóa đơn hàng. Các nguồn gồm ScheduledCheck, ManualCheck, Callback, DailyCallback, InvoiceLookup, CancellationCheck và QrRecoveryCheck; giá trị enum đã cố định để lưu bền. Callback lấy mã biên nhận nội bộ từ inbox đã xác thực, không nhận nguồn do client gửi. CompleteAsync chỉ ghi khoản thu và liên kết PaymentId, không thay đổi nguồn đã lưu. Lookup nối nguồn theo PaymentId, phân biệt xác minh và ghi khoản thu; nguồn cũ null giữ nguyên. QR thủ công dùng thông tin xác nhận đã lưu trên PosPaymentQrRequest. Popup hóa đơn có cột Phương án xác nhận và thông tin tương tự trong từng QR.

Đã bổ sung callback danh sách giao dịch (API 8175): TRANSACTION_UPDATE / TRANSACTION_HISTORY, nhận tối đa 1.000 dòng/trang, lưu từng dòng, phân biệt các đợt gửi, chống trùng, xử lý lại sau lỗi và báo thiếu trang. Màn Đối soát QR ACB ở `/admin/acb/reconciliation`, cài đặt x-api-key theo cửa hàng. Migration `20260908192844_AddAcbQrNotificationReconciliation` và script nâng cấp `docs/acb-qr-list-upgrade.sql` đã tạo. Phiếu nghiệm thu cập nhật: [acb-acceptance-tests.md](acb-acceptance-tests.md). Các mục phía dưới ghi lại quá trình triển khai trước đó.

Đã xác định và sửa lỗi POS báo nhầm giỏ thay đổi sau khi nhận tiền: JsonSerializer giữ scale của decimal, nên giá trị tổng do Recalc là 9000 khác chuỗi với SQL decimal(18,2) là 9000.00. Dấu kiểm tra mới chuẩn hóa số mà không làm tròn giá trị. QR cũ được đối chiếu với đúng cách biểu diễn cũ; không ghi đè dấu kiểm tra hoặc bỏ điều kiện đơn/ca/máy/số tiền. Chỉ phục hồi lỗi đối chiếu giỏ sau một lần tra cứu ACB mới có giao dịch hợp lệ; các lý do giữ khác vẫn giữ nguyên.

Đơn thử 15 đã có giao dịch ACB 15100 COMPLETED 5.000đ cho QR GA0000000006. Khi chẩn đoán, OrderPayment vẫn chỉ có tiền mặt 4.000đ; không sửa trực tiếp dữ liệu thanh toán. Bản sửa cần chạy lại tại POS để nghiệm thu chốt/in thực. 150 ca .NET nghiệp vụ/HTTP, 3 ca SQL (khóa và decimal), 6 ca JavaScript đều đạt. Callback qua Internet vẫn chờ nghiệm thu trên host.

## Callback sau khi đối chiếu tài liệu ACB ngày 08/09/2026

Đã đọc tài liệu trong phiên ACB Developer do người dùng đăng nhập. API tức thời là **Thông báo trạng thái giao dịch QR Code**, phiên bản 1.0.5. API **Thông báo danh sách giao dịch QR Code** tổng hợp ngày T-1 là luồng khác. Nguồn: [đặc tả callback](https://developer.acb.com.vn/acb/open/vi/product/10489/api/8178), [khởi tạo](https://developer.acb.com.vn/acb/open/vi/product/10489/api/8043), [tra cứu](https://developer.acb.com.vn/acb/open/vi/product/10489/api/9765), [hướng dẫn đăng ký](https://developer.acb.com.vn/acb/open/vi/getting-started).

Người dùng xác nhận URL đã đăng ký với ACB cho cửa hàng chính là **https://www.gaomart.com.vn/Admin/api-callback**. Code nhận POST tại đúng đường dẫn này, đồng thời giữ alias `/api/acb/webhook`. Bản StoreId cho phép chọn cửa hàng đích tại `/admin/acb/callback-routing`, không cần đổi subdomain cửa hàng thành `www` và không tự chọn StoreId = 1. URL công khai vẫn phải được reverse proxy/IIS chuyển tới ứng dụng mới, giữ host gốc. Chưa sửa DNS, IIS, dữ liệu cửa hàng hoặc đăng ký dịch vụ bên ACB trong lần làm việc này.

ACB hỗ trợ API key qua `Authorization: <api_key>`; dùng chính chế độ này khi cấu hình với ngân hàng. Key do doanh nghiệp cấp và được mã hóa riêng từng cửa hàng trong ứng dụng. `masterMeta.clientId` trong callback do đối tác cung cấp; tài liệu không khẳng định nó trùng ClientId OAuth gọi API đi. Không tự ghép hai giá trị này. Code hiện không xác thực bằng Basic/JWT; Bearer với khóa tĩnh và X-Api-Key được giữ để tương thích endpoint ban đầu.

Callback được kiểm tra loại `NOTIFICATION` với `TRANSACTION_UPDATE` hoặc `TRANSACTION_HISTORY`, định danh request và trang, rồi lưu nguyên nội dung vào `AcbCallbackReceipts` cùng các dòng thông báo trước khi trả HTTP 200 với mã `00000000`, echo requestTrace và referenceCode ổn định. Gửi lại cùng cửa hàng/loại/request/page không tạo biên nhận thứ hai; cùng định danh nhưng dữ liệu nghiệp vụ khác trả 409, giữ bản gốc. Transport trace/thời gian/checksum có thể đổi khi gửi lại mà không tạo trùng dữ liệu. Sai key trả 403; sai cấu trúc trả 400. Thời gian phản hồi theo offset ACB công bố (`+0700` / `+0000`).

Worker trên máy chủ được đánh thức ngay sau khi lưu callback. Nó lấy chứng cứ từ retrieve, đối chiếu QR và giao dịch, cập nhật trạng thái rồi đẩy hub về máy gốc. Không phụ thuộc việc trình duyệt còn mở hay hết đếm ngược. Nếu ACB chưa cập nhật kết quả hoặc kết nối lỗi, biên nhận vẫn ở hàng chờ, tự thử lại với thời gian lùi tối đa 60 giây. Một lượt quét phục hồi chạy mỗi 5 giây để tiếp tục công việc sau khởi động lại hoặc khi mất tín hiệu nội bộ. Có khóa SQL riêng theo biên nhận và đơn để các instance không xử lý trùng.

**Vẫn xác minh bằng retrieve trước khi ghi nhận tiền.** Tài liệu callback có checksum nhưng chưa mô tả công thức và chưa chỉ rõ cách ghép mã callback với `transactionDetail.transactionNumber`. Code lưu checksum, không tự đoán thuật toán hoặc coi checksum chưa kiểm chứng là chữ ký hợp lệ. Điều này không buộc thu ngân bấm kiểm tra: máy chủ tự xác minh ngay theo thông báo. Nếu muốn bỏ hoàn toàn bước retrieve, cần ACB xác nhận công thức checksum và ánh xạ mã giao dịch trước.

Thông báo `ERRORCORRECTED`, sai cửa hàng/máy/tài khoản, số tiền lệch hoặc nhiều thông báo thành công trong cùng batch cho một QR chuyển sang Cần kiểm tra. Không tự xóa payment, hoàn tiền hay đảo chứng từ của đơn đã chốt. Callback chưa ghép được QR được giữ và đánh dấu `UNMATCHED_QR`. Trang cài đặt có thời điểm nhận callback gần nhất, số đang chờ xử lý và số chưa ghép được QR để theo dõi kết nối.

Đã sửa mã hóa đơn gửi ACB thành `GA` + Id yêu cầu QR toàn hệ thống (10 chữ số), tổng 12 ký tự, theo giới hạn 13 ký tự của ACB. MerchantId và tên thụ hưởng giới hạn 30 ký tự. Không tái sử dụng dải Id yêu cầu QR khi chuyển database; mã tham chiếu này giả định các cửa hàng dùng chung database GaoApp hiện tại.

## Quy tắc đã thống nhất

- Tất cả dòng hàng còn hiệu lực, kể cả hàng tặng, phải có `ProductVariant.HasInputInvoice = true` để dùng QR động.
- Số tiền QR là tổng tiền đơn trừ các khoản đã nhận còn hiệu lực. Đơn 100.000đ đã nhận tiền mặt 30.000đ tạo QR 70.000đ.
- Đơn không đủ điều kiện hoặc cửa hàng chưa bật ACB tiếp tục dùng QR thủ công hiện có.
- Mỗi lần tạo QR lưu riêng cửa hàng, đơn, ca, máy, nhân viên, số tiền, trạng thái đơn tại lúc tạo và mã tham chiếu ACB. Tạo lại sau khi hủy dùng mã tham chiếu mới.
- Hủy QR đang chờ phải được ACB xác nhận; các khoản tiền mặt đã nhận vẫn giữ nguyên, đơn vẫn là Draft.
- Một giao dịch ngân hàng thành công đúng số tiền được xử lý về máy/ca gốc để ghi nhận tiền, chốt đúng mã đơn và in bill.
- Chuyển lệch số tiền, nhiều giao dịch, tiền về sau hủy, đổi nội dung đơn, đổi ca/máy hoặc ca đóng đều chuyển sang Cần kiểm tra; không tự chốt/in.

## Cấu hình

1. Áp dụng migration `20260908151919_AddStoreAcbPayments` và `20260908155844_AddAcbCallbackInbox` bằng quy trình GaoApp.Migrator hiện có. Chúng tạo ba bảng ACB ban đầu và bảng biên nhận `AcbCallbackReceipts`, cùng các khóa/chỉ mục. Chưa áp lên database vận hành.
2. Đăng nhập đúng cửa hàng, vào Tài khoản ngân hàng → Cài đặt ACB tự động (`/admin/acb/settings`). Người cấu hình cần quyền `system.integration.manage`.
3. Chọn tài khoản ACB đang hoạt động của cửa hàng. Điền TokenEndpoint, ApiBaseUrl, QrEndpoint, ClientId, ClientSecret, TokenScope, XService, XProviderId, XOwnerNumber, XOwnerType, VirtualAccountPrefix, MerchantId, BeneficiaryName và CallbackApiKey theo hợp đồng ACB.
4. Cửa hàng chính dùng URL đã đăng ký `https://www.gaomart.com.vn/Admin/api-callback`, API-key mode qua header Authorization. Cửa hàng khác dùng subdomain riêng. Khi chuyển ứng dụng mới lên URL này, cần xác minh proxy giữ nguyên host và Authorization, và ACB thực sự gửi callback về ứng dụng mới.
5. Bật QR tự động sau khi cấu hình xong. Mặc định tính năng tắt. Biểu mẫu mặc định sandbox; các endpoint phải cùng môi trường ACB đã được cấp quyền.

Client secret và callback key được mã hóa bằng ASP.NET Core Data Protection với purpose riêng từng cửa hàng. Ô mật khẩu để trống khi sửa sẽ giữ nguyên giá trị. Cần giữ bền vững key ring đang dùng của ứng dụng, và chia sẻ key ring giữa các instance nếu triển khai nhiều máy chủ. Không có credential thật trong thay đổi này.

Máy tính tiền cần quyền xem thanh toán (`pos.payment.view`) và chốt đơn (`pos.order.finalize`). Quyền tạo thanh toán được dùng khi hủy QR tại trang tra cứu. Dùng cơ chế đăng ký máy POS hiện có.

## Xác nhận và khôi phục

Webhook xác thực, lưu vào hàng chờ bền vững rồi phản hồi ACB. Worker tự truy vấn chứng cứ và gửi hub; polling và callback dùng chung mã giao dịch retrieve để tránh ghi tiền hai lần. Nếu chưa lưu được biên nhận, HTTP trả lỗi để ngân hàng có thể gửi lại. Nếu đã lưu và trả thành công, worker chịu trách nhiệm thử lại đến khi xử lý được hoặc đánh dấu dữ liệu cần kiểm tra.

POS nhận thông báo `acb_payment_changed` qua nhóm cửa hàng/máy. Màn hình chờ kiểm tra dự phòng sau 30 giây, tiếp theo mỗi 8 giây. Sau khi tải lại trang, POS tìm lại các QR của máy/ca gốc. Thao tác chốt luôn dùng OrderId đã lưu, không dùng giỏ hiện tại của trình duyệt.

Khóa `sp_getapplock` theo cửa hàng/đơn tuần tự hóa tạo, hủy, đối soát và chốt qua ACB giữa các instance. Bản ghi thanh toán và liên kết ACB được lưu trong cùng transaction. Nếu phần chốt đơn lỗi, lần thử sau dùng lại khoản đã nhận. Kiểm tra ACB được chạy lại bên trong transaction chốt đơn sau khi tính khuyến mại để ngăn chốt một giỏ đã thay đổi.

Server ghi nhận quyền phát lệnh in một lần. Không thể xác minh máy in đã thực sự nhả giấy bằng `window.print()`. Nếu trình duyệt mất kết nối ngay sau khi nhận quyền in hoặc máy in lỗi, dùng chức năng in lại đơn hiện có. Đây là giới hạn của cơ chế in trình duyệt.

Nếu yêu cầu tạo QR bị gián đoạn, bản ghi giữ trạng thái Đang xác minh tạo QR để tránh phát sinh QR thứ hai khi chưa biết kết quả ngân hàng. Tra cứu và hủy QR cũ tại đúng máy trước khi tạo lại. Tiền về sau khi ca đóng được lưu để kiểm tra.

## Tra cứu

Từ chi tiết đơn chọn Tra cứu chuyển khoản ACB, hoặc mở `/admin/acb/payments/orders/{OrderId}`. Trang liệt kê từng lần tạo QR, trạng thái nội bộ, số tiền yêu cầu, ngày tạo, ghi chú cần kiểm tra và từng giao dịch ngân hàng gồm mã, số tiền thực chuyển, trạng thái, nội dung, ngày hạch toán nguyên bản ACB trả về. Nút Tra cứu ACB cập nhật chứng cứ/trạng thái QR nhưng không tự ghi payment, chốt hoặc in.

## Kiểm chứng trước vận hành

Các kiểm thử tự động dùng ngân hàng giả lập và cấu trúc callback đã đối chiếu tài liệu ACB. Kiểm thử HTTP dùng Kestrel cục bộ với host www, tenant middleware thật và API key chỉ dành cho test. Cần kiểm thử sandbox/thực tế với cấu hình được ACB cấp, đường callback công khai, token, quy cách merchant/order/trace, quyền của tài khoản thu ngân, máy in và việc gửi lại webhook trước khi bật tại quầy. Chưa thực hiện giao dịch ngân hàng hoặc áp migration lên dữ liệu vận hành trong lần triển khai code này.

## Kết quả kiểm thử ngày 08/09/2026 — sau bổ sung callback

- Build thành công, không có lỗi; 9 cảnh báo hiện có ở các test UI/báo cáo ngoài phạm vi ACB.
- 38 kiểm thử nghiệp vụ, callback HTTP trên Kestrel cục bộ và hồi quy POS đạt.
- 77 kiểm thử migration/schema/bootstrap và khóa SQL đạt, gồm kiểm tra khóa biên nhận vẫn giữ khi giải phóng khóa đơn lồng bên trong. Các test SQL dùng LocalDB và database thử tạm.
- 2 kiểm thử JavaScript đạt bằng `node --test GaoApp.Tests/Ui/pos-acb-realtime.test.cjs`, xác nhận hub đến khi đang chờ HTTP vẫn được xử lý tiếp ngay mà không kích hoạt timer dự phòng.
- Tổng 117 kiểm thử đã chọn đạt. Chưa triển khai migration lên dữ liệu vận hành, chưa nhận callback từ ACB thật hoặc thử máy in thật.

## Thông tin cần xác minh cùng ACB trước vận hành

- URL đã đăng ký có được ACB kích hoạt thông báo QR tức thời trong đúng môi trường hay chưa; yêu cầu gửi một callback kiểm thử và kiểm tra referenceCode phản hồi.
- Cách ACB gửi lại khi nhận 400/409/5xx, thời gian timeout và giới hạn số lần thử. Ứng dụng tự phục hồi những callback đã lưu; thông báo chưa đến máy chủ vẫn cần ACB gửi lại hoặc truy vấn dự phòng.
- Công thức checksum, byte/encoding đầu vào và ánh xạ traceNumber/referenceNumber với mã giao dịch trong retrieve nếu cần xác nhận trực tiếp hoàn toàn từ callback.
- IIS/App Pool hoặc dịch vụ host phải giữ ứng dụng hoạt động để worker nhận và xử lý kịp thời. Máy/ca gốc cần mở POS để hoàn tất chốt và in; tiền về khi ca đóng chuyển Cần kiểm tra.

Chưa gửi phiếu hỗ trợ, thay đổi đăng ký tại ACB, gọi API bằng credential thật hay thực hiện chuyển tiền thử. Người dùng không cần gửi mật khẩu hoặc khóa bí mật vào chat.

## Kiểm thử từ localhost — bổ sung theo yêu cầu người dùng

Chưa cần đưa ứng dụng lên host để gọi API từ máy local ra ACB Sandbox. Người dùng thống nhất kiểm thử các API gọi đi trước; callback thực từ ACB sẽ kiểm thử khi đưa lên host. Không mở tunnel hay thay đổi URL callback đang đăng ký trong giai đoạn này. Sandbox là môi trường kiểm thử được ACB cấp quyền, không được coi một phản hồi Sandbox là bằng chứng đã nhận tiền thật vào tài khoản.

Môi trường Development của project dùng cổng HTTP 5100, HTTPS 7051 và `Tenant:RootDomain = localhost`. Ví dụ cửa hàng `demo` truy cập bằng `http://demo.localhost:5100`; dùng đúng subdomain thực tế của database đang chạy. Địa chỉ localhost không thể đăng ký để ACB gọi trực tiếp từ Internet. Trang cài đặt hiển thị rõ khi endpoint đang là local.

Các bước thử trong `/admin/acb/settings`:

1. Lưu bộ cấu hình Sandbox do ACB cấp và tài khoản nhận tiền của cửa hàng; các endpoint đều thuộc `sandbox.acb.com.vn`. Có thể để tính năng QR tự động tắt trong lúc kiểm tra kết nối. Client secret và callback key nhập vào biểu mẫu, không dán vào chat hoặc commit vào source.
2. Bấm **Kiểm tra kết nối ACB**. Thao tác chỉ xin token; thành công chỉ chứng minh kết nối xác thực. Token không trả về trình duyệt.
3. Chọn máy của cửa hàng rồi bấm **Thử tạo → tra cứu → hủy QR Sandbox**. Phép thử cấp mã riêng `TS...` (13 ký tự), tạo QR 1.000đ, gọi retrieve và hủy QR đó. Không tạo Order, payment, xuất kho hoặc in bill. Không hiển thị ảnh QR để khách quét.
4. Xem từng bước Đạt/Chưa đạt và mã tham chiếu. API retrieve có thể trả mảng rỗng khi chưa có giao dịch thanh toán; không được đánh dấu đã thu tiền chỉ vì gọi retrieve thành công.
5. Khi đã thử được kết nối ACB thật trên Sandbox, chạy đơn thử qua POS để kiểm tra điều kiện HDDV, phần còn phải trả, các nhánh hủy/lỗi và đúng máy/ca. Luồng nhận callback vẫn dùng kiểm thử giả lập ở local cho đến khi host sẵn sàng.

Phép thử tạo/hủy bị chặn trước mọi request nếu bất kỳ endpoint nào thuộc Production. Mã tham chiếu thử được ghi vào `App_Data/Logs/acb-sandbox/<RunId>.json` trước khi gọi ACB; nhật ký không chứa ClientSecret, API key, token, ảnh QR hoặc nguyên phản hồi ngân hàng. Khi mất phản hồi tạo QR hoặc tra cứu lỗi, hệ thống vẫn thử hủy QR theo mã đã lưu. Nếu hủy chưa thành công, kết quả báo Chưa đạt và giữ mã/trace để xử lý; không tự khẳng định QR đã hủy.

Kiểm tra chỉ đọc ngày 08/09/2026 trên `GaoAppDb` tại máy local `DESKTOP-E059ENS\SQLEXPRESS` (đích được ghi công khai trong appsettings.json): đã có bảng ACB, cửa hàng đang hoạt động `demo` (Id 1) chưa có StoreAcbSettings. Cổng HTTP 5100 chưa phản hồi tại thời điểm kiểm tra. Chưa gọi API ACB bằng bộ credential thật vì chưa có cấu hình Sandbox được lưu trong database này. Nếu người dùng chạy database khác, cần xác định lại đúng database/cửa hàng qua giao diện.

Nguồn: [ACB hướng dẫn cấp dữ liệu kiểm thử](https://developer.acb.com.vn/acb/open/vi/getting-started), [đặc tả hủy QR](https://developer.acb.com.vn/acb/open/vi/product/10489/api/8792).

## Sửa lỗi lưu cấu hình khi chưa chọn tài khoản ACB

Ảnh lỗi `The value '' is invalid.` xuất phát từ select BankAccountId gửi chuỗi rỗng vào thuộc tính int không nullable. Biểu mẫu đã dùng int? cùng thông báo Required/Range bằng tiếng Việt; entity lưu database vẫn bắt buộc tài khoản hợp lệ. Không bỏ kiểm tra tài khoản để vượt lỗi.

Danh sách và kiểm tra khi lưu thống nhất chỉ nhận tài khoản ACB đang hoạt động của cửa hàng hiện tại, không phân biệt hoa thường mã ACB. Trang chỉ rõ trường bắt buộc và hướng dẫn thêm tài khoản nếu danh sách trống. Giá trị cấu hình thông thường giữ lại khi gửi lỗi; Client secret/Callback API key đã nhập không hiển thị lại trong HTML. Nếu khóa chưa từng được lưu thành công, người dùng cần nhập lại khi gửi biểu mẫu hợp lệ.

Build cùng bộ kiểm thử chọn lọc mới nhất đạt 53/53: nghiệp vụ ACB/callback, POS UI, kiểm tra token, tạo/tra cứu/hủy Sandbox bằng ngân hàng giả lập, cùng hồi quy MVC binding/lưu cấu hình. Bao gồm chuỗi rỗng/0, tài khoản khác ngân hàng hoặc ngừng hoạt động, lưu ACB hợp lệ và giữ khóa đã lưu khi ô khóa để trống. Chưa gọi Sandbox ACB thực bằng bộ thông tin người dùng: cần lưu cấu hình hợp lệ qua giao diện trước. Callback ACB thực tiếp tục để kiểm thử trên host theo yêu cầu.

## Chuyển sang ACB Production theo yêu cầu người dùng — 09/09/2026

Người dùng xác nhận dùng endpoint token vận hành thật. Đã lưu qua giao diện đăng nhập của cửa hàng demo trên local:

- TokenEndpoint: `https://openapi-iam.acb.com.vn/acb/open/iam/id/v1/auth/realms/soba/protocol/openid-connect/token`
- ApiBaseUrl: `https://openapi.acb.com.vn`
- QrEndpoint: `https://openapi.acb.com.vn/acb/open/payments/qr-payment/v1/initiate`

Đã bấm Kiểm tra kết nối ACB trên ứng dụng local với khóa đã lưu. Ứng dụng phản hồi **Kết nối xác thực ACB Production thành công**: đây là lần gọi token thực tới ACB, không phải fake HTTP handler. Không đọc/hiển thị token hoặc Client secret. Kết quả này chỉ xác nhận token, chưa chứng minh quyền tạo QR hoặc callback.

Đã thêm POST `/admin/acb/settings/test-production`, dùng cùng quyền quản lý tích hợp, antiforgery và máy đang hoạt động của đúng cửa hàng. Nút Production chỉ hiện khi bộ endpoint đã lưu thuộc Production. Phép thử tạo QR 1.000đ với mã riêng `TP...`, truy vấn đúng mã, rồi hủy mã đó; không hiển thị QR, không tạo Order/payment, không chốt/in. Nhật ký `App_Data/Logs/acb-production-check/<RunId>.json` giữ mã/trace và kết quả từng bước. Luồng Sandbox vẫn chặn endpoint Production. Kiểm tra khi lưu cũng chặn trộn hai môi trường.

Thông báo lỗi nay giữ mã HTTP, mã lỗi OAuth nằm trong danh sách cho phép và mã nghiệp vụ ACB 8 chữ số; không trả nguyên responseMessage/error_description/body/token của ngân hàng. Lỗi token không bị biến thành một thông báo chung trong trang kết quả.

65/65 kiểm thử tự động chọn lọc đạt (ngân hàng giả lập cho tạo/tra cứu/hủy), gồm Production thành công, mất phản hồi tạo, tra cứu lỗi, hủy lỗi, chặn nhầm môi trường, phân loại lỗi DNS/TLS/kết nối và không lộ dữ liệu bí mật trong thông báo/nhật ký.

Người dùng đã chạy lại ứng dụng local. Đã cập nhật BeneficiaryName từ số tài khoản thành HO KINH DOANH DOAN THANG LOI, đúng tên đã khai báo trong danh mục tài khoản ACB của cửa hàng. Đã gửi phép thử Production qua giao diện với máy Quay thu ngan 01. Lần thử dừng ở bước lấy token do lỗi kết nối; chưa gửi yêu cầu tạo QR. Mã tham chiếu nội bộ TP9803C2CE6A9, RunId 9f9e5d26-6c7a-490f-a6be-f75909f875bf. Nhật ký xác nhận chỉ có bước lấy token thất bại, không có bước tạo/tra cứu/hủy. Lần kiểm tra token độc lập ngay sau đó cũng lỗi kết nối. Kiểm tra mạng không gửi khóa: DNS phân giải được endpoint ACB, nhưng HTTP request hết thời gian chờ và TCP tới cổng 443 timeout. Chưa kết luận nguyên nhân phía đường truyền/firewall/ACB. Cần kết nối phục hồi để hoàn tất thử API tạo/tra cứu/hủy thực; không coi các API này đã đạt. Luồng xác nhận nhận tiền và callback thực chưa được kiểm thử.

Callback từ ACB vẫn sẽ kiểm thử trên host theo yêu cầu; chưa đổi đăng ký callback `https://www.gaomart.com.vn/Admin/api-callback`.


## Kiểm thử trên POS và khôi phục lần tạo gián đoạn — 09/09/2026

Sau lần lỗi kết nối ở trên, người dùng cung cấp kết quả Production đạt đủ bốn bước token → tạo QR 1.000đ → tra cứu → hủy. Nhật ký tương ứng `App_Data/Logs/acb-production-check/0863d762d8c94131bb2051a75d48a84c.json`, mã `TPE63B407879E`. Phép thử này chưa chuyển tiền, chưa xác nhận callback.

Đã thử tại POS cửa hàng demo, đơn 15, ca 1, máy 1: một sản phẩm 9.000đ, có sẵn tiền mặt 4.000đ, còn phải chuyển khoản 5.000đ. Lần thử bị chặn bởi QR `GA0000000002` đang Creating từ lần lấy token timeout trước đó. Log xác định lỗi xảy ra tại GetAccessTokenAsync, trước khi gửi initiate. Tra cứu ngân hàng thành công với `orders: []`, `totalPages: 0`, `totalRows: 0`; API hủy trả `30020402`. Đã đối chiếu bảng mã lỗi trên trang ACB đăng nhập: mã này là `Data not found`.

Bản sửa:
- Lỗi ACB đã lọc dữ liệu nhạy cảm được trả về POS bằng thông báo cụ thể, không còn chỉ “Có lỗi hệ thống xảy ra”. Tra cứu/hủy giữ thông báo này.
- Lỗi lấy token dạng AcbApiException trước khi gửi initiate hủy riêng lần tạo nội bộ, giữ lịch sử và tiền mặt, cho phép thử lại. Mất phản hồi sau khi đã gửi initiate vẫn giữ Creating để đối soát.
- Với lần Creating chưa có ảnh QR hoặc tài khoản ảo, chỉ cho hủy khi lần tra cứu mới hoàn tất không tìm thấy đơn, API hủy trả đúng 30020402 và không có bất kỳ bằng chứng giao dịch đã lưu. QR đã hiển thị, tra cứu lỗi/sai cấu trúc, mã lỗi khác hoặc có bằng chứng giao dịch đều không được giải phóng theo nhánh này. Callback đến muộn vẫn được lưu và đưa vào kiểm tra.
- POS hiển thị đường dẫn xem giao dịch của đúng đơn khi phục hồi một lần Creating.

Đã đạt 97 kiểm thử .NET chọn lọc (ACB, POS UI contract, middleware) và 3 kiểm thử JavaScript (hub đến trong lúc HTTP đang chạy, phục hồi discovery, thông báo Creating). Chưa thay đổi trạng thái đơn/tiền mặt trực tiếp bằng SQL. Đã chạy lại ứng dụng và tạo thành công QR thực trên POS. Callback ngân hàng thật tiếp tục để kiểm thử trên host.

Nguồn mã lỗi: [ACB Giải pháp QR động, Danh sách mã lỗi](https://developer.acb.com.vn/acb/open/vi/product/10489/api/8792).


## Nâng cấp callback, kiểm tra thủ công và tra cứu hóa đơn — 09/09/2026

Người dùng xác nhận đã đăng ký callback bằng **header `x-api-key`**. Endpoint `/Admin/api-callback` ưu tiên header này; vẫn tương thích Authorization theo đặc tả ACB, nhưng từ chối nếu hai header cùng có giá trị khác nhau. Không ghi giá trị khóa/token vào log. ACK thành công chỉ trả sau khi lưu callback vào inbox; bản tin trùng trả lại mã nhận cũ, bản tin xung đột bị từ chối. Ngân hàng lỗi/JSON không hợp lệ được giữ để thử lại và ghi mã lỗi theo giai đoạn. Bản tin không ghép được QR được giữ để đối chiếu.

Nhật ký riêng dạng JSONL: `GaoApp.Web/App_Data/Logs/acb-callback/callback-YYYYMMDD.jsonl`, giới hạn 10 MB/file, giữ tối đa 30 file. Mỗi HTTP callback có DiagnosticId, StoreId, HTTP status, outcome, ReceiptId nếu đã lưu, thời gian xử lý và tên header. Xử lý nền ghi mã nhận, clientRequestId, trang, số lần thử và mã lỗi; chỉ AcbApiException đã lọc dữ liệu được phép ghi thông báo ngân hàng an toàn. Không ghi body/Authorization/API key vào file. Payload callback hợp lệ vẫn lưu tại bảng AcbCallbackReceipts để đối soát theo cửa hàng.

Đã kiểm tra trên tiến trình local thực: POST một khóa sai do bài kiểm tra tự tạo tới callback trả HTTP 403, `AUTH_REJECTED`, header X-Acb-Diagnostic-Id; đúng sự kiện xuất hiện trong file `callback-20260909.jsonl`, StoreId 1, AuthHeader x-api-key và không có giá trị khóa thử trong log. Đây là phép thử chẩn đoán đầu vào local, không phải callback từ ACB qua Internet.

Nút **Kiểm tra ngay / F9** tại popup gọi tra cứu ngân hàng cho QR hiện tại, có trạng thái đang kiểm tra, chưa đủ tiền hoặc lỗi cụ thể. Yêu cầu bấm kiểm tra trong lúc một HTTP khác đang chạy được xếp lại và chờ đúng lượt kiểm tra mới; không bị bỏ qua. Có tiền đúng điều kiện thì dùng chung luồng ghi nhận/chốt/in của callback; chưa có tiền thì giữ nguyên đơn và tiền mặt. Callback lỗi hoặc localhost chưa nhận được callback vẫn có thể kiểm tra bằng nút này. F9 trên QR tự động không còn hiển thị nhãn xác nhận tiền thủ công.

Danh sách `/admin/pos/orders-page` và chi tiết hóa đơn có nút **Chuyển khoản ACB** mở cửa sổ trong trang. Nội dung hiển thị tổng số lần tạo, số giao dịch, trạng thái lần mới nhất, số tiền từng QR, ngày tạo/tra cứu, ca/máy và bảng giao dịch thực nhận. Nội dung ngân hàng được hiển thị bằng textContent. Tra cứu trên trang hóa đơn không trực tiếp gọi chốt/in; chốt/in vẫn do POS gốc xử lý.

Kiểm thử cuối: 101 ca .NET đạt, 6 ca JavaScript đạt; gồm cả HTTP callback trên Kestrel local bằng dữ liệu giả với x-api-key và Authorization, sai khóa, sai JSON, xung đột header, lỗi lưu inbox, ngân hàng trả JSON lỗi rồi phục hồi, không callback nhưng kiểm tra thủ công nhận tiền/chốt một lần, cùng kiểm tra hồi quy trước đó. Không gửi callback giả xác nhận nhận tiền vào dữ liệu vận hành.

Đã xem trực quan cửa sổ tra cứu trên ứng dụng thực và tạo QR Production mới **GA0000000005**, 5.000đ, cho đơn 15 (tổng 9.000đ, tiền mặt đã thu 4.000đ), ca 1/máy 1. Giữ QR mới mở cho người dùng thử chuyển tiền. Các QR thử trước GA0000000002/3/4 đều đang Đã hủy khi kiểm tra. Chưa chuyển tiền thực, chưa chốt/in trong lần nâng cấp này. Hướng dẫn bàn giao: [acb-local-and-host-testing.md](acb-local-and-host-testing.md).
