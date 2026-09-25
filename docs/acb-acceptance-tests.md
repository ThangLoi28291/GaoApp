# Nghiệm thu QR POS — nhiều lần thanh toán, 09/09/2026

## Đã hoàn thành trong code

- Giữ URL callback cũ, chọn cửa hàng bằng **StoreId** tại `/admin/acb/callback-routing`. Chỉ quản trị toàn hệ thống được chọn; lưu lịch sử đổi, kiểm tra phiên cấu hình cũ, khóa cửa hàng đích và giữ liên kết khi đổi subdomain. Chưa chọn/ngừng nhận/store không hoạt động trả 503 và ghi mã chẩn đoán. Xem [hướng dẫn chọn StoreId và bài test R01–R06](acb-callback-store-routing.md).
- QR ACB và QR thủ công mặc định bằng phần còn thiếu; thu ngân có thể nhập số tiền nhỏ hơn để khách dùng nhiều tài khoản thanh toán. Số tiền mỗi QR phải là số đồng nguyên dương và không vượt số còn thiếu tại lúc tạo. ACB áp dụng khi bật cấu hình và toàn bộ hàng, kể cả hàng tặng, có hóa đơn đầu vào. QR thủ công lấy tài khoản đang hoạt động được đặt mặc định của cửa hàng.
- Nhận **Thông báo trạng thái giao dịch QR Code** và **Thông báo danh sách giao dịch QR Code**; cùng URL `https://www.gaomart.com.vn/Admin/api-callback`, POST JSON, xác thực header `x-api-key` đã thống nhất với ACB. Giữ tương thích API key trong `Authorization` theo tài liệu ACB.
- Nhận `TRANSACTION_UPDATE` tức thời và `TRANSACTION_HISTORY` cuối ngày T−1. Chuẩn danh sách có thêm transactionChannel, transactionDate, debitOrCredit và transactionContent. Ngày hiệu lực dùng định dạng yyyy-MM-dd. Báo nợ và ERRORCORRECTED cần đối chiếu.
- Lưu biên nhận và từng dòng trước khi trả thành công; nhận tối đa 1.000 dòng/trang, giới hạn HTTP 4 MiB. Kiểm tra trang hợp lệ và tổng trang nhất quán trong mỗi đợt. Nhận sai thứ tự được; thiếu trang có cảnh báo.
- Gửi lại cùng loại bản tin + clientRequestId + trang không tạo thêm dòng. Thay đổi trace, thời gian gửi và checksum khi gửi lại vẫn được nhận nếu dữ liệu nghiệp vụ không đổi; nội dung nghiệp vụ khác trả 409 và giữ bằng chứng gốc. Tức thời và cuối ngày được phân biệt ngay cả khi mã yêu cầu trùng.
- Máy chủ tra cứu ACB để xác minh số tiền. Ngân hàng lỗi sẽ thử lại; một QR lỗi không chặn tra cứu các QR còn lại trong cùng trang. Callback không trực tiếp cộng tiền vào hóa đơn.
- Chỉ đúng ca và máy POS tạo QR được chốt. Thanh toán và quyền in được lưu để chống trùng. Đơn đã chốt không bị ghi thêm khoản thu/in thêm khi ACB gửi danh sách cuối ngày.
- Đối soát tại `/admin/acb/reconciliation`: ngày hiệu lực, loại bản tin, phân trang; số ACB thông báo, số xác minh, số ghi hóa đơn; trạng thái khớp/chờ xác minh/chưa ghép/lệch tiền/cần đối chiếu/chưa ghi khoản thu/chưa chốt. Xem tiến độ nhận trang theo đợt. Quản trị tích hợp có thể đưa trang vào hàng đợi xử lý lại.
- Nút chuyển khoản chỉ có trên đơn có chuyển khoản hoặc lịch sử QR. Popup hiển thị giao dịch ACB, khoản thu thủ công và QR đã hủy; không cộng QR hủy vào tiền đã thu.
- Popup có **Phương án xác nhận** trên từng giao dịch và trong chi tiết từng QR: tự kiểm tra theo thời gian, thu ngân bấm Kiểm tra ngay, callback tức thời/cuối ngày. Lưu thời điểm xác minh, người thao tác cho kiểm tra bằng tay, mã biên nhận cho callback. Tra cứu tại hóa đơn, kiểm tra trước hủy và phục hồi QR được ghi đúng tên nguồn riêng. QR thủ công hiển thị nguồn thu ngân xác nhận.
- Nguồn được lưu cùng lần đầu xác minh hợp lệ, trước bước ghi khoản thu; các lần check/callback/chốt đến sau không ghi đè. Từng lần thanh toán trên cùng đơn có nguồn riêng. Giao dịch ACB cũ chưa có dữ liệu nguồn hiển thị **Chưa lưu nguồn xác nhận**, không suy đoán từ log hay lần tra cứu gần nhất. Có phân biệt **Đã xác minh · Chưa ghi khoản thu**.
- Ngay trong Thanh toán có **Mở lại QR gần nhất** và danh sách từng lần tạo. Mở lại dùng GET đọc đúng ảnh, mã và số tiền đã lưu; không gọi initiate, không sửa số tiền nhập và không tạo khoản thu. Lịch sử vẫn có sau tải lại trang/đổi phương thức. QR đã hủy hoặc đã ghi nhận tiền vẫn mở lại ảnh và thông tin, có cảnh báo chỉ xem lịch sử và khóa thao tác thu tiền. Nút **Tạo QR mới** tạo lần riêng; số tiền nhập không bị đổi khi mở lại mã cũ.
- Cài đặt riêng từng cửa hàng; nhóm Callback có URL, header và ô **Giá trị x-api-key**. Để trống giữ khóa cũ; khóa lưu mã hóa, không hiển thị lại.
- Log HTTP/worker tại `App_Data/Logs/acb-callback/callback-YYYYMMDD.jsonl`; chứa DiagnosticId, ReceiptId, RequestCode, Page/TotalPages, mã kết quả và lần thử. Không ghi API key, access token hoặc payload ngân hàng vào log chữ.

Nguồn đã đối chiếu sau khi đăng nhập: [Danh sách giao dịch QR, API 8175](https://developer.acb.com.vn/acb/open/vi/product/10489/api/8175), [Trạng thái QR, API 8178](https://developer.acb.com.vn/acb/open/vi/product/10489/api/8178). Trường checksum được giữ trong bằng chứng; tài liệu đã đọc chưa mô tả thuật toán để kiểm tra checksum. Xác thực dùng khóa và xác minh tiền qua API tra cứu có OAuth.

## Kiểm thử tự động đã chạy

Kết quả bộ thanh toán hiện tại: **240 ca .NET nghiệp vụ/HTTP/Razor/tenant/proxy + 32 ca JavaScript POS**. **3 ca khóa/decimal SQL Server** đã đạt ở lần kiểm tra trước; lần StoreId này không thay đổi cơ chế khóa và không chạy lại nhóm SQL đó. Các ca ngân hàng dùng giả lập, không gửi tiền hoặc callback giả vào dữ liệu bán hàng. EF xác nhận model và migration khớp nhau; đã sinh script nâng cấp. Chưa áp dụng migration lên database đang bán hàng và chưa nghiệm thu giao diện bản mới trong trình duyệt đang chạy; màn chọn StoreId đã render trong kiểm thử Razor.

Kiểm tra schema toàn hệ thống có thử thêm nhưng không hoàn tất vì môi trường SQL thử nghiệm của bộ đó không kết nối được; không tính vào kết quả trên.

Các nhóm đã kiểm tra: điều kiện hàng tặng/HDDV; tiền mặt trước QR; token/create/retrieve/cancel và lỗi mạng; xác thực callback; ACK sau lưu; 1.000 dòng; trang đảo thứ tự/thiếu trang; gửi lại/xung đột; tức thời + cuối ngày chống trùng tiền/in; báo nợ/điều chỉnh; sai tiền; QR không tồn tại; retry sau lỗi; ghép QR xuất hiện muộn; sai cửa hàng; đúng ca/máy; khóa SQL giữa hai kết nối; UI check thủ công khi callback thiếu.

Hồi quy bổ sung: 9000 và 9000.00 cùng giá trị không làm thay đổi dấu kiểm tra giỏ; vẫn phát hiện thay đổi thực sự, kể cả phần lẻ rất nhỏ; kiểm tra lại QR cũ đang bị giữ nhầm; mở lại thanh toán dùng QR cũ; chốt một lần sau phục hồi; giữ chặn nếu ngân hàng lỗi, thiếu giao dịch hiện tại, sai trace, ca đóng hoặc có điều chỉnh ngân hàng.

Nguồn xác nhận: 19 ca .NET mới kiểm tra từng đường xác minh, lưu/đọc lại qua hóa đơn, thời điểm/người thao tác/biên nhận, callback trước bằng chứng ngân hàng, lỗi/sai tiền không chiếm nguồn, nguồn đầu tiên không bị ghi đè, dữ liệu cũ không bị gán lại và từng khoản trả riêng. 3 ca JavaScript mới chạy màn tra cứu với DOM giả lập, kiểm tra nguồn tại bảng/lịch sử, giữ nguồn khi refresh, phân biệt QR thủ công và an toàn khi tên người dùng chứa HTML.

Chạy lại từ thư mục dự án:

```powershell
.\scripts\test-acb-payments.ps1 -SqlServer 'DESKTOP-E059ENS\SQLEXPRESS'
```

Nếu chỉ chạy bộ giả lập, bỏ tham số `-SqlServer`. Cần .NET 8, Node.js và các package đã restore. Kết quả .NET nằm ở `TestResults/acb/acb-tests.trx`. Script không gửi callback thử vào ứng dụng bán hàng.

## Chuẩn bị bản mới

1. Build/chạy lại ứng dụng bằng source mới. Migration callback mới nhất: `20260909062834_AddAcbCallbackStoreRouting`, thêm bảng chọn cửa hàng và lịch sử thay đổi. Không chọn sẵn StoreId, không gán nguồn cho giao dịch cũ và không sửa số tiền đơn hàng.
2. Kiểm tra migration đã áp dụng khi ứng dụng khởi động. Nếu host chạy migration riêng, dùng quy trình triển khai hiện có; script riêng phần chọn cửa hàng là `docs/acb-callback-store-routing-upgrade.sql`. Bản cũ hơn vẫn cần đầy đủ migration trước đó, gồm `AddAcbConfirmationAudit` và `AddPosQrInstallmentLinks`.
3. Vào **Cài đặt thanh toán ACB → Callback ACB · Xác nhận tự động**. Khóa phải trùng giá trị ACB gửi qua `x-api-key`. Lưu ở GaoApp không tự đăng ký thêm dịch vụ tại ACB.
4. Trên host, yêu cầu ACB bật cả thông báo tức thời và danh sách cuối ngày cho URL HTTPS đã đăng ký. Cấu hình IIS/reverse proxy cho phép POST JSON tới callback và body đến 4 MiB; giữ worker chạy, lưu bền khóa Data Protection.

## Bài test thao tác trên localhost

| Mã | Thao tác | Kết quả đạt |
|---|---|---|
| L01 | Mở danh sách hóa đơn, xem một đơn chỉ tiền mặt/thẻ | Không có nút chuyển khoản. Chi tiết hóa đơn vẫn có lịch sử thanh toán bình thường. |
| L02 | Mở đơn có CK thủ công | Có popup; khoản thu ghi rõ thu ngân xác nhận, không gắn nhãn ACB xác minh. |
| L03 | Đơn 9.000đ đủ điều kiện HDDV, thu tiền mặt 4.000đ, chọn CK | QR đúng 5.000đ; chưa chuyển tiền thì chưa chốt. |
| L04 | Có một hàng thường hoặc hàng tặng thiếu HDDV | Không đi luồng QR ACB tự xác nhận. |
| L05 | Khi chưa chuyển tiền, bấm Kiểm tra ngay hoặc F9 | Hiện kết quả chưa nhận tiền; giữ đơn và tiền mặt, không tạo khoản thu giả. |
| L06 | Khách chuyển đúng số tiền vào QR còn hiệu lực rồi bấm Kiểm tra ngay | ACB xác nhận, ghi đúng một khoản CK, chốt đúng đơn/ca/máy, in một lần. Đây là bước người dùng chuyển tiền thật. |
| L07 | Sau L06 bấm check/tải lại; xem hóa đơn và popup CK | Không cộng thêm tiền hoặc chốt/in trùng; thấy giao dịch, nội dung và mã ACB. |
| L08 | Tạo một QR chưa trả tiền, bấm Hủy QR | Sau khi ACB xác nhận hủy, giữ tiền mặt đã thu; QR hủy vẫn nằm trong lịch sử. |
| L09 | Mở Đối soát QR ACB, chọn ngày hiệu lực, chọn Tức thời/Cuối ngày/Tất cả | Lọc đúng loại/ngày. Chưa có callback thật thì bảng trống là đúng, không phải lỗi. |
| L10 | Vào cài đặt, kiểm tra ô x-api-key | Thấy nhóm Callback, header x-api-key, trạng thái khóa đã lưu; ô mật khẩu không lộ khóa cũ. |
| L11 | Đóng popup hoặc tải lại POS, mở Thanh toán → Mở lại QR gần nhất | Hiện đúng QR cũ, cùng mã/ảnh/số tiền; không cần nhập tiền hoặc Enter và không sinh lệnh ACB mới. |
| L12 | Mở Lịch sử QR, chọn từng lần tạo | Mỗi lần có mã/số tiền/thời gian/trạng thái riêng. Chọn mã nào mở đúng mã đó; QR đã hủy/đã xong mở ảnh và thông tin ở chế độ chỉ xem, không có xác nhận/hủy. |
| L13 | Đổi sang đơn khác trong lúc đang tải QR, hoặc bấm mở liên tiếp | Không hiển thị QR của đơn trước; bấm lặp không tạo yêu cầu mới. |

| L14 | Đơn còn thiếu 5.000đ, nhập 2.000đ → Tạo QR mới → chuyển/kiểm tra | Ghi đúng 2.000đ, đơn chưa chốt, không in; ô tiền mặc định còn 3.000đ. |
| L15 | Tiếp L14, tạo QR 3.000đ, chuyển từ tài khoản khác rồi kiểm tra | Hai QR và hai khoản chuyển riêng; tổng đủ thì chốt/in một lần. |
| L16 | Lặp L14–L15 với QR thủ công | Đúng tài khoản mặc định; thu ngân kiểm tra ngân hàng rồi xác nhận từng QR. Bấm lại không cộng tiền trùng. |
| L17 | Tạo sẵn hai QR lần lượt 2.000đ và 3.000đ, mở từng mã từ lịch sử | Đúng ảnh/mã/số tiền từng lần, có thể xác nhận theo thứ tự tiền về. |
| L18 | Tạo thêm một QR chưa sử dụng, dùng QR khác thanh toán đủ | Hệ thống tra cứu trước khi hủy QR ACB thừa. Nếu chưa có tiền và ACB xác nhận hủy, chốt bình thường; tiền về/không rõ giữ để đối chiếu. QR thủ công chưa dùng được kết thúc khi đơn chốt. |
| L19 | Nhập số tiền QR vượt số còn thiếu hoặc có phần lẻ đồng | Từ chối, không tạo QR. |
| L20 | Mở lại QR đã thu hoặc đã hủy, thử F9/F10 | Chỉ xem lịch sử, không thu lại hoặc hủy khoản đã thu. |
| L21 | Thu 2.000đ QR rồi 3.000đ tiền mặt để đủ đơn | Chốt ở bước tiền mặt; cập nhật ACB sau đó không yêu cầu in lần thứ hai. |

| L22 | Mở QR ACB mới, chờ; bấm Kiểm tra ngay; mở lại QR; thử lỗi kết nối | Đếm 00:30 lần đầu, hiện Đang kiểm tra khi gọi ACB, sau phản hồi tiếp tục 00:08. Check thủ công bỏ qua chờ. Mở lại cùng mã không đặt lại thời gian. Lỗi có đếm thời gian thử lại. QR thủ công/đã kết thúc không hiện đồng hồ tự kiểm tra. |

| L23 | Sau khi khởi động bản mới, để một khoản QR ACB được nhận qua đếm giờ; vào hóa đơn → Chuyển khoản | Cột Phương án xác nhận ghi Tự kiểm tra theo thời gian cùng thời điểm. Chỉ kiểm tra với giao dịch mới được xác minh sau migration. |
| L24 | Với một khoản khác, bấm Kiểm tra ngay sau khi chuyển tiền và trước lần kiểm tra tự động | Nếu lần bấm là lần xác minh hợp lệ đầu tiên, ghi Thu ngân bấm Kiểm tra ngay, thời điểm và tên người thao tác. Nếu tự kiểm tra đã nhận trước đó thì giữ nguồn tự kiểm tra. |
| L25 | Tra cứu lại hóa đơn, tải lại trang, chốt/check lại một khoản đã ghi nhận | Nguồn và thời điểm ban đầu không đổi; không phát sinh thêm tiền hoặc in lại. Các khoản QR của cùng đơn hiển thị nguồn riêng. |
| L26 | Xem QR thủ công đã xác nhận và giao dịch ACB hoàn tất trước bản cập nhật | QR thủ công ghi thu ngân xác nhận; ACB cũ ghi Chưa lưu nguồn xác nhận, không tự gán nguồn khi bấm tra cứu. |
| L27 | Chưa chuyển tiền, bấm kiểm tra; nếu ACB báo lỗi thì kiểm tra lại sau | Không ghi một phương án xác nhận thành công trước khi có bằng chứng ngân hàng hợp lệ. |

L01–L27 là phiếu nghiệm thu để người dùng đánh dấu sau khi chạy; không coi bộ test giả lập là bằng chứng tiền thật đã về.

### Giao dịch đang nghiệm thu của đơn 15

Người dùng đã chuyển 5.000đ vào QR **GA0000000006**; kết quả tra cứu đã lưu giao dịch **15100**, trạng thái **COMPLETED**, số tiền **5.000đ**. Tại thời điểm chẩn đoán, hóa đơn vẫn chỉ có tiền mặt 4.000đ vì lỗi đối chiếu dạng số 9000/9000.00. Đây là lỗi ứng dụng, không phải thiếu tiền tại ACB.

Sau khi build/chạy bản sửa, giữ đúng ca/máy tạo QR, mở **Thanh toán → Mở lại QR gần nhất → Kiểm tra ngay**. Hoặc mở Lịch sử QR rồi chọn GA0000000006. Không cần nhập số tiền hay Enter để mở lại. Không chuyển thêm tiền, không hủy QR đã nhận tiền, không tạo QR khác. Người dùng đã xóa khoản tiền mặt 5.000đ nhập thêm; dữ liệu xác nhận chỉ còn tiền mặt 4.000đ. Kết quả cần nghiệm thu: một khoản ACB 5.000đ, tổng đã thu 9.000đ, chốt/in một lần, popup hóa đơn có giao dịch 15100. Chưa coi việc sửa code/test giả lập là bằng chứng đơn thực đã chốt.

## Bài test khi đưa lên host

| Mã | Thao tác | Kết quả đạt |
|---|---|---|
| H01 | Chuyển đúng tiền từng QR, giữ POS tạo QR đang mở | Callback đến URL đã đăng ký; ghi nhận từng khoản. Chỉ khi tổng đủ mới chốt/in đúng máy, không cần bấm check. Nếu callback xác minh hợp lệ trước các lần check, hóa đơn ghi Callback ACB · Tức thời và mã biên nhận. |
| H02 | Yêu cầu ACB gửi lại cùng thông báo, kể cả đổi trace/thời gian gửi | Một biên nhận nghiệp vụ và bộ dòng thông báo; không thêm payment, không in lại. |
| H03 | ACB gửi TRANSACTION_HISTORY cho ngày trước | Đối soát chọn ngày hiệu lực hôm trước thấy bản tin cuối ngày; đơn đã chốt hiện Đã khớp. |
| H04 | Đợt có nhiều trang; một trang tới trễ | Hiện đã nhận a/b trang; khi trang thiếu đến thì đủ b/b, không nhân bản trang đã nhận. |
| H05 | Một giao dịch có trên ACB nhưng chưa ghi khoản thu/chưa chốt | Hiện đúng trạng thái; tra cứu tại đúng POS. Ca đóng, QR hủy hoặc đơn đã đổi phải giữ để đối chiếu. |
| H06 | Kiểm tra kịch bản ERRORCORRECTED/báo nợ hoặc lệch tiền theo bộ thử ACB | Cần đối chiếu, không tự hoàn tiền hoặc sửa đơn đã chốt. |
| H07 | Mô phỏng API tra cứu ACB tạm lỗi trong môi trường kiểm thử | ACK vẫn thành công sau lưu; worker thử lại; QR khác trong cùng trang tiếp tục được kiểm tra. |
| H08 | Thử header sai/thiếu trong môi trường kiểm thử | HTTP 403, không lưu bản tin; tra được DiagnosticId trong log. |
| H09 | Callback chứa mã QR không có tại cửa hàng | Lưu lại, hiển thị Chưa ghép QR; không tra hoặc ghi vào cửa hàng khác. Có thể Xử lý lại trang sau khi kiểm tra dữ liệu. |
| H10 | Xem log HTTP, worker và tiến độ nhận trên màn cài đặt | Tra cứu được theo mã biên nhận/loại/trang; không có khóa/token trong log. |

Các H01–H10 cần xác nhận trên HTTPS public và cấu hình thông báo thực tế của ACB. Đừng gửi payload giả vào dữ liệu bán hàng thật để mô phỏng tiền đã về; các kịch bản giả lập đã có trong bộ test tách biệt.


Bổ sung đếm ngược: đồng hồ dùng cùng thời điểm với lịch tra cứu thực tế, giữ kiểm tra trạng thái callback định kỳ và xử lý hub ngay. Không thêm migration. Tám ca JavaScript mới kiểm tra lịch 30/8 giây, bấm thủ công, lỗi mạng, nhiều QR, trạng thái dừng và quay lại tab nền; tổng JavaScript là 29 ca.
