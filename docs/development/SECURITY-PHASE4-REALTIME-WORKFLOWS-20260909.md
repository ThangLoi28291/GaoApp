# Đợt 4 — Thu hồi SignalR và kiểm thử nghiệp vụ có đăng nhập

> Cập nhật đợt 5: xem [chống trùng thu tiền và kiểm thử POS](SECURITY-PHASE5-PAYMENT-IDEMPOTENCY-20260909.md). Hai test giao diện và phần thu tiền từng lần được xử lý tiếp trong báo cáo đó; nội dung dưới giữ kết quả lịch sử của đợt 4.


Phạm vi được duyệt: mục 1–2, bổ sung thu hồi kết nối realtime đang mở và kiểm thử luồng nghiệp vụ/phân quyền qua Web thật. Không backup dữ liệu test. Không reset database hay khởi động lại ứng dụng người dùng đang chạy; các test sở hữu database LocalDB tên ngẫu nhiên và tiến trình Web riêng. Đợt này không thêm migration.

## Thay đổi bảo mật

`PosRealtimeSessionValidator` kiểm tra lại tài khoản, membership, cửa hàng, terminal, vai trò, dấu phiên và quyền từ SQL. `RevocablePosHubLifetimeManager` kiểm tra người nhận trước khi gửi thông báo qua các đường connection/group/user/all; kết nối hết quyền bị đóng. Dấu quyền gồm ID bản ghi cấp quyền và mã quyền, nên gỡ rồi cấp lại cùng quyền vẫn làm phiên đang mở mất hiệu lực khi quan sát được thay đổi.

Filter kiểm tra quyền trước mỗi lời gọi Hub. Riêng `BroadcastTerminalEvent` cần quyền `Pos.Payment.Create` và chỉ nhận sáu loại sự kiện hiển thị khách hàng đang được JavaScript sử dụng; client không được giả sự kiện nghiệp vụ server như `order_finalized`. Nhóm cửa hàng/terminal phải khớp cookie. Đăng xuất đóng các kết nối của cùng người dùng/cửa hàng/terminal/dấu phiên trong tiến trình hiện tại.

Worker kiểm tra kết nối nhàn rỗi mỗi 5 giây, với hạn kiểm tra 5 giây. Khi không xác minh được quyền do lỗi SQL hoặc hết hạn, đóng các kết nối thuộc lượt kiểm tra và không gửi dữ liệu. Thời điểm đóng kết nối nhàn rỗi phụ thuộc lịch chạy và thời gian SQL; đây không phải SLA thời gian thực cứng. Kiểm tra trước khi gửi độc lập với lịch worker. Thông báo đã gửi trước khi thay đổi quyền không thể thu hồi.

Bật `CloseOnAuthenticationExpiration` để đóng kết nối khi vé đăng nhập hết hạn. Cơ chế này được bổ sung kiểm tra quyền hiện tại ở trên, vì hết hạn cookie và thu hồi quyền là hai điều kiện khác nhau. API áp dụng từ .NET 6 và Hub filter từ .NET 5, có trong runtime .NET 8 của app; xem [Microsoft — CloseOnAuthenticationExpiration](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.connections.httpconnectiondispatcheroptions.closeonauthenticationexpiration?view=aspnetcore-9.0) và [Hub filters](https://learn.microsoft.com/en-us/aspnet/core/signalr/hub-filters?view=aspnetcore-10.0).

Trong kiểm thử phát hiện policy quyền đặt trực tiếp trên method Hub phụ thuộc TenantContext của middleware HTTP, nhưng scope invocation không có tenant đó. Quyền phát sự kiện được kiểm tra trong Hub filter bằng danh tính kết nối và quyền đọc mới từ SQL. Policy xem POS vẫn kiểm tra ở handshake HTTP.

Các controller POS được bổ sung quyền riêng cho tạo/sửa đơn, thanh toán, hoàn tất, giữ đơn, giảm giá, in lại, hủy và hoàn tiền. Hoàn tiền cần cả quyền hoàn đơn và hoàn thanh toán; endpoint vừa thu tiền vừa hoàn tất cần cả hai quyền tương ứng. Các thao tác thay đổi trạng thái của POS và ca bán hàng được bảo vệ bằng antiforgery. Các role tùy chỉnh thiếu quyền sẽ nhận 403; cần kiểm tra phân công quyền thực tế trước rollout.

## Kiểm thử và phạm vi bằng chứng

- `FullWorkflowSqlServerTests`: Web entry point, middleware tenant, cookie thật, CSRF thật, controller, service, SQL và WebSocket thật. Khóa tài khoản/membership, đổi password/role, gỡ quyền chính/quyền khác, cấp lại quyền; đóng kết nối nhàn rỗi; khóa cửa hàng/terminal; giới hạn nhóm; từ chối viewer phát sự kiện; chống giả sự kiện server; logout và duy trì kết nối người dùng còn quyền.
- Luồng nghiệp vụ: đăng nhập quản lý/viewer/hai cửa hàng → mở ca → tạo đơn, bán 3 đơn vị giá 20 → thu 60 → hoàn tất → trả lại 1 đơn vị, hoàn 20 → tạo đơn thứ hai, thu 40 và hủy → đóng ca đối soát 40. Tồn từ 100 còn 98; cửa hàng thứ hai còn 100 và không nhận sự kiện của cửa hàng thứ nhất. Kiểm tra từ chối truy cập trái quyền, cookie chuyển host, thiếu CSRF; gửi lại lệnh hoàn tất/hủy không ghi tồn lần nữa.
- `PosRealtimeBatchSqlServerTests`: 10/20/50 tài khoản độc lập được kiểm tra quyền trong một lượt với 3 SQL read; khóa một tài khoản chỉ loại phiên của tài khoản đó. Đây là kiểm tra chi phí truy vấn và tính đúng, không phải benchmark 50 WebSocket đồng thời. Code chia batch tối đa 100 phiên; chi phí mỗi sự kiện còn phụ thuộc số người nhận và tần suất phát.
- Hồi quy thêm: phiên HTTP, tenant, dữ liệu, quan sát lỗi, payment, POS/ca, nhập/xuất kho đồng thời và giá vốn/hủy/hoàn. Không gọi ngân hàng thật.

Kết quả đã chạy ngày 09/09/2026 trên .NET SDK 8.0.420/runtime 8.0.29 và SQL LocalDB:

| Nhóm | Kết quả | Bằng chứng |
|---|---|---|
| Script nghiệp vụ/bảo mật cuối, gồm 12 Web workflow + 3 batch + 9 SQL hồi quy | 24/24 đạt, 0 skipped; 2 phút 19 giây | `TestResults/security-phase4/phase4-script-workflows.trx`, `Logs/security-phase4-script-workflows.log` |
| Hồi quy rộng | 525/527 đạt, 2 fail giao diện nêu dưới, 0 skipped; 2 phút 59 giây | `TestResults/security-phase4/phase4-final-regression.trx`, `Logs/security-phase4-final-regression.log` |
| Release build cuối | 0 lỗi | `Logs/security-phase4-release-build.log` |

Bộ hồi quy rộng đã chạy trên implementation cuối của Web, trước khi bổ sung 3 test batch và assertion đóng ca. Lượt script 24/24 sau đó chạy lại các test liên quan cùng các bổ sung này. Hai bộ có các test trùng nhau, không cộng tổng thành số test độc lập. Build đầy đủ trước đó có 10 warning ở test đã có; lượt build tăng dần cuối không phát sinh warning. Đã kiểm tra whitespace của các file liên quan. Script chạy thật với `-SkipBuild` trên artifact vừa build; fixture đã dọn database và thư mục runtime riêng sau test.

Hai test `PosPrimeResponsiveUiContractTests.Styles_should_select_prime_or_legacy_exclusively_with_shared_pos_foundation` và `Index_outside_styles_and_two_conditional_adapter_seams_should_match_closed_reference` đang fail ở bộ hồi quy rộng. Test thứ nhất chỉ chấp nhận `pos.css` trong phần CSS chung, trong khi trang hiện có thêm `pos.qr-history.css`; test thứ hai báo hash nội dung trang khác mẫu cũ. Các file giao diện và hai test này không được sửa trong đợt 4. Cần rà soát thay đổi giao diện QR/ACB so với mẫu được duyệt và cập nhật contract hoặc sửa UI theo kết quả đó; không đổi hash chỉ để test xanh.

## Chạy lại

Máy Windows có .NET 8, Node cho một số test JavaScript, và SQL LocalDB:

```powershell
./scripts/test-security-workflows.ps1
# Hồi quy rộng, gồm cả hai test giao diện đang fail được nêu trên:
./scripts/test-security-workflows.ps1 -IncludeRegression
```

Script build solution Release riêng tại `Logs/security-phase4-release`. `-SkipBuild` chỉ dùng nếu artifact khớp source hiện tại. Test Web yêu cầu cả DLL Web và test từ cùng artifact; không chạy chỉ build riêng project test. Không chạy build vào cùng artifact trong khi vstest đang dùng DLL. TRX và log Web fixture nằm trong `TestResults/security-phase4`.

## Giới hạn và việc tiếp theo trước production

1. Áp dụng bản build mới bằng quy trình rollout bình thường; tiến trình ứng dụng đang mở vẫn dùng code trước đợt sửa. Không cần giữ dữ liệu test để chạy các test này.
2. Chốt hai contract giao diện POS đang lệch, rồi UAT bằng trình duyệt với các role thực tế, scanner/in hóa đơn/màn hình phụ và kiểm tra ACB sandbox. HTTP/WebSocket automation chưa thay thế thao tác DOM hay tích hợp ngân hàng thật.
3. Chống gửi lặp đã được chứng minh cho hoàn tất/hủy đơn, các nghiệp vụ kho và nhóm ACB có test riêng. API thêm thanh toán tiền mặt từng phần chưa có khóa idempotency theo request; chưa được chứng nhận an toàn cho mọi trường hợp mất mạng/gửi lại cùng lần thu. Cần bổ sung cơ chế đó và kiểm thử trước khi tuyên bố chống trùng thanh toán toàn diện.
4. Triển khai staging với HTTPS/proxy/SQL/media/keys như đợt 2; đo lại 10/20/50 client từ máy khác sau khi thêm kiểm tra realtime. Số đo đợt 3 không bao gồm chi phí này, TLS hoặc hạ tầng host. Theo dõi SQL, reconnect và tải dài hạn.
5. Lifetime manager hiện dùng transport trong một tiến trình. Nếu chạy nhiều web instance hoặc thay bằng Redis/Azure SignalR, phải thiết kế lại thu hồi và phân phối liên instance; không chỉ thêm backplane rồi giữ nguyên giả định này. Logout hiện không tạo danh sách thu hồi cookie bền vững để chống mọi tình huống phát lại cookie bị đánh cắp.

Hoàn thành phạm vi kiểm thử này chưa đồng nghĩa toàn bộ ứng dụng đã hết lỗi hoặc đủ điều kiện mở production.
