# Rà soát lại bảo mật và tính hoàn chỉnh — 23/10/2026

**Chưa thể xác nhận đã xử lý hết mọi lỗi và rủi ro.** Các lỗi đã sửa đạt bộ hồi quy nội bộ, nhưng phần 3 về bảo mật tra cứu khách trên kiosk vẫn đang tạm hoãn; công cụ Android còn hai điểm cần xử lý trước khi coi là bản vận hành hoàn chỉnh. Phần 14 về phục hồi toàn bộ hệ thống chưa được nghiệm thu trên hạ tầng vận hành.

Lượt này kiểm tra và đánh giá, không sửa mã ứng dụng, thay đổi menu/quyền hay triển khai. Kết nối ngân hàng, hóa đơn, AI và máy in tiếp tục được mô phỏng. GaoAppDb chỉ được đọc metadata trong lượt này; các kiểm thử tự tạo và dọn CSDL GUID riêng.

## Phạm vi và bằng chứng

- Kiểm kê/quét 2.656 tệp đang dùng trong Web, Application, Domain, Infrastructure, Migrator, AutoInvoiceWorker, hai thành phần in, Android, công cụ và cấu hình/script. Có 1.990 C#, 254 Razor, 213 JavaScript, 90 PowerShell và 27 Java. Các bản build, dữ liệu runtime, dependency đã cài và gói migration lưu trữ lịch sử được phân biệt khỏi source ứng dụng.
- Đối chiếu SHA-256 của 3.726 tệp trong danh mục bằng chứng cuối lượt sửa trước: không đổi. Vì vậy kết quả hồi quy đã hoàn tất vẫn áp dụng cho cùng source: **4.749 backend, 673 JavaScript, 47 kịch bản trình duyệt đạt**. Đây là kết quả lượt trước, không phải một lần chạy lại toàn bộ trong lượt rà soát này.
- Quét các vị trí cần kiểm tra ngữ cảnh: 240 lời gọi bỏ query filter, 10 lời gọi mở system scope, SQL thô, tải tệp, HTTP ra ngoài và HTML động. Đây là danh mục để truy vết, không phải số lỗ hổng. Review tập trung vào các ranh giới quyền, tenant, thanh toán, tệp, log, dữ liệu offline và triển khai; không khẳng định mọi dòng của mọi tệp đã được chứng minh an toàn.
- Audit NuGet của 9 project trong solution và npm của ba dự án có dependency hiện hành đều hoàn tất, không báo lỗ hổng trong kết quả trả về. 59 tài nguyên trình duyệt khớp checksum của manifest. Không suy ra từ đây rằng không tồn tại lỗ hổng chưa công bố, hoặc mọi thành phần OS/IIS/SQL và dịch vụ ngoài đã được kiểm tra.
- Kiểm tra mới: **158/158 backend thuộc 26 lớp bảo mật**, **46/46 JavaScript** và **482 assertion Java** đạt. Không có ca thất bại hoặc bỏ qua. Có cả HTTP, SQL Server và một ca Chrome thật của link hóa đơn trong bộ backend này.

Bằng chứng riêng: `.artifacts/security-recheck-20261023/`. Kết quả hồi quy và gói publish đã hoàn tất: [SYSTEM-FIXES-20261023.md](SYSTEM-FIXES-20261023.md).

## Những điểm còn tồn tại, xử lý riêng từng vấn đề

### R1 — Cao: Kiosk vẫn xác định khách hàng chỉ bằng số điện thoại

**Trạng thái:** tương ứng phần 3 cũ, người dùng đã yêu cầu tạm hoãn. Chưa có bản sửa và không được tính là đã đóng.

**Bằng chứng:** `GaoApp.Web/Controllers/KioskController.cs:91–148`. Trên kiosk đã kích hoạt, POST số điện thoại khớp một hồ sơ sẽ đặt `CustomerId`, trả tên, điện thoại, điểm và thống kê mua hàng; các API tiếp theo trả lịch sử, chi tiết hàng và voucher. Cookie kiosk, CSRF, giới hạn tra cứu và thời hạn hai phút bảo vệ thiết bị/phiên, chưa xác minh chủ số điện thoại. Test hiện có `Customer_exact_phone_lookup_is_read_only_private_and_expires` chủ động kiểm tra chính hành vi này, nên test đạt không chứng minh vấn đề đã được sửa.

**Ảnh hưởng:** người đứng ở kiosk hợp lệ và biết số điện thoại người khác có thể xem dữ liệu của họ. Các API tra cứu này không trực tiếp đổi điểm hoặc dùng voucher.

**Phương án A:** xác minh OTP trước khi tạo phiên khách. Không trả hồ sơ, lịch sử, chi tiết đơn hoặc voucher trước xác minh; tránh lộ việc số điện thoại có đăng ký. OTP dùng một lần, hạn ngắn, giới hạn đoán/gửi lại; phiên gắn đúng khách/cửa hàng/kiosk, có hạn tuyệt đối và hạn không thao tác. Tất cả API con phải kiểm tra phiên; đóng phiên phải xóa màn hình và chặn phản hồi cũ.

**Phương án B:** tạm khóa toàn bộ tra cứu hồ sơ/lịch sử/voucher trên kiosk; nhân viên hỗ trợ tại POS. Giữ chức năng tra giá và mua hàng. Cách này không cần dịch vụ OTP.

**Nghiệm thu:** người chỉ biết số điện thoại không lấy được dữ liệu; sửa ID, dùng phiên hết hạn/đã thu hồi, đổi cửa hàng/kiosk đều bị chặn; khách đã xác minh tra cứu đúng. Chọn A cần chốt dịch vụ gửi OTP và chi phí trước khi code.

### R2 — Trung bình, có điều kiện: APK Android đang là bản debug và token lưu trực tiếp

**Bằng chứng:** `tools/AcbNotificationProbeAndroid/app/src/main/AndroidManifest.xml:23` bật `debuggable=true`; `Build.ps1:104–117` dùng khóa debug và không có nhánh release vận hành. `WebBridge.java:40,88` lưu token trong SharedPreferences riêng của ứng dụng. Đọc APK hiện có bằng aapt xác nhận bản `0.2.5-bridge`, versionCode 12, có `application-debuggable`; không chỉ là cờ trong source chưa được đóng gói. README cũng xác định đây là bản thử nghiệm.

**Ảnh hưởng:** nếu dùng APK này với khóa thật, người có khả năng debug hoặc kiểm soát thiết bị dễ tiếp cận dữ liệu và khóa hơn. Đây là điểm gia cố phát hành có điều kiện; chưa chứng minh có tấn công từ Internet hoặc ứng dụng khác tự đọc được private preferences trên máy bình thường. Android khuyến nghị tắt debug khi phát hành. [Android Developers](https://developer.android.com/privacy-and-security/risks/android-debuggable)

**Phương án A:** tách build thử nghiệm và build vận hành; bản vận hành tắt debug, dùng khóa ký được quản lý riêng và có kiểm tra APK đầu ra. Mã hóa token bằng khóa trong Android Keystore; thiết kế nâng cấp dữ liệu cũ an toàn, bảo toàn cấu hình và hàng đợi. Xem xét thu hồi/cấp lại token từng thiết bị đã dùng bản thử với dữ liệu thật. Không đổi cơ chế tự xác nhận Android đã được người dùng chấp thuận.

**Nghiệm thu:** APK release không debug, không dùng khóa thử; cài nâng cấp theo chữ ký được chấp thuận giữ dữ liệu/hàng đợi; đọc tệp cấu hình không thấy token rõ; thu hồi token cũ chặn được request tiếp theo. Cần xem xét chữ ký đang cài trước khi đổi khóa để tránh phải gỡ app và mất hàng đợi.

### R3 — Trung bình: Android bỏ thông báo mới khi hàng đợi đủ 1.000 tin

**Bằng chứng:** `WebBridge.java:134–143`, `enqueue()` trả về trước lệnh INSERT khi `count() >= 1000`; chỉ cập nhật thông báo trạng thái. `ProbeStore` giữ tối đa 200 bản nội dung để quan sát, không phải hàng đợi dự phòng. Khi xem/xuất lịch sử, code chủ động không tự gửi lại lịch sử. Quét lại chỉ có thể thấy thông báo Android còn giữ, không bảo đảm khôi phục mọi tin đã bỏ.

**Ảnh hưởng:** khi mất mạng, token lỗi hoặc server không nhận đủ lâu để hàng đợi đầy, tin mới không được lưu vào outbox. Có nguy cơ khách đã chuyển tiền nhưng POS không tự nhận được thông báo. Điều này không tự làm mất tiền trong ngân hàng và chưa có bằng chứng đã xảy ra trên điện thoại thật.

**Phạm vi cần phân biệt:** đây là giới hạn sức chứa **1.000 tin chưa gửi trên điện thoại**, không phải giới hạn tổng đơn trong ngày. Luồng callback ACB trực tiếp ở server vẫn giữ phương án đã chốt: xác thực → lưu inbox thành công → ACK, callback hợp lệ không bị hard-limit bằng bộ đếm tiếp nhận.

**Phương án A:** lưu bền vững bằng chứng cần gửi, không âm thầm bỏ tin mới vì một ngưỡng số dòng cố định; chỉ dọn bằng chứng đã được ACK theo chính sách. Có ngưỡng cảnh báo sớm, dung lượng/tuổi hàng đợi, trạng thái lỗi lưu trữ và quy trình đối soát khi không thể lưu. Không thể hứa tiếp nhận vô hạn khi ổ đĩa hết chỗ; phải báo rõ thất bại và giữ dấu vết thiếu. Bảo toàn mã chống trùng và không phát lại tự động lịch sử quan sát chưa được xác nhận là giao dịch mới.

**Nghiệm thu:** hơn 1.000 thông báo hợp lệ trong lúc mất kết nối, khởi động lại rồi nối mạng phải gửi đủ bằng chứng đã lưu; không thu tiền trùng. Mô phỏng đầy ổ đĩa/lỗi SQLite phải báo lỗi có thể nhận biết, không báo đã lưu/gửi thành công. Lượt này xác nhận nhánh lỗi bằng truy vết source; chưa dựng phép thử SQLite Android cho ngưỡng đầy hàng đợi.

## Các rủi ro đã được chấp thuận và giới hạn vận hành

**Phần 5 — tiếp tục tự xác nhận từ Android:** giữ theo quyết định trước của người dùng. `BankNotificationBridgeService` vẫn có `NotificationAuto`. Token/thiết bị bị kiểm soát có thể tạo bằng chứng thông báo giả phù hợp khoản chờ; trường `SourcePackage` do bên gửi khai báo chưa tương đương bằng chứng ngân hàng độc lập. Chống trùng, token, phạm vi cửa hàng và đối chiếu đơn không loại bỏ được rủi ro nguồn thông báo. R2 chỉ gia cố ứng dụng; không tự biến thông báo thành xác nhận trực tiếp của ngân hàng. Không xếp việc giữ tính năng này là lỗi triển khai trái yêu cầu.

**Dữ liệu offline:** phần 7 đã giảm dữ liệu tải xuống, khóa phiên và dọn bản đã đồng bộ. Giao dịch chưa đồng bộ còn phải lưu trên máy, chưa mã hóa toàn bộ IndexedDB ở tầng ứng dụng. Người kiểm soát hồ sơ trình duyệt/thiết bị vẫn có thể đọc bản sao; thu hồi từ server không tới ngay thiết bị đang mất mạng. Đây là giới hạn của phương án A đã chốt, không được xóa journal chưa đồng bộ chỉ để làm sạch dữ liệu.

**Hạn mức và nhiều instance:** bộ đếm trong GaoApp áp dụng theo tiến trình. Hạn mức tổng của nhiều máy và tác vụ AI tại ChatbotLocal cần lớp điều phối tương ứng. Callback ACB hợp lệ được ưu tiên lưu bền vững; hệ thống vẫn cần bảo vệ lưu lượng bất thường ở hạ tầng. Hạn mức sai khóa chạy sau bước kiểm tra credential nên chưa loại bỏ chi phí SQL/xác thực của mọi request sai.

**Phần 14 — phục hồi:** đã có script kiểm tra backup, diễn tập DB GUID và các test tương thích khóa/cookie; chưa có bằng chứng một lần phục hồi đầy đủ hệ thống vận hành từ DB + uploads/XML + key ring + certificate/private key + cấu hình bằng danh tính dịch vụ thực. Chưa chốt/đo RPO, RTO và nghiệm thu ACL/quyền SQL trên máy chủ. Cần thực hiện phần 14 riêng trước khi ghi là hoàn tất vận hành; không suy ra backup production tốt chỉ vì restore bản sao local đạt.

## Đối chiếu 14 phần của kế hoạch cũ

| Phần | Kết luận hiện tại |
|---|---|
| 1 — quản trị nhân viên/quyền | Đã sửa trong source; kiểm tra quản trị từ DB, mật khẩu xác minh, bảo vệ ADMIN cuối, audit và thu hồi phiên có test. |
| 2 — QR/trạng thái màn hình khách | Đã sửa trong source; server dựng trạng thái, chặn payload QR/thành công giả. |
| 3 — hồ sơ khách kiosk | **Chưa sửa, tạm hoãn theo yêu cầu; R1 còn mở.** |
| 4 — link thông tin hóa đơn | Đã sửa hạn xem/sửa, thu hồi, chống cache/referrer và che token trong log ứng dụng. Log IIS/proxy cần nghiệm thu riêng. |
| 5 — độ tin cậy thông báo Android | **Giữ tự xác nhận theo chấp thuận; rủi ro nguồn thông báo còn. R2 và R3 là các điểm gia cố riêng.** |
| 6 — thu hồi thiết bị POS | Đã ràng buộc cookie với thiết bị và kiểm tra lại HTTP/SignalR. Giữ cách chọn quầy/ghép mới đã chốt. |
| 7 — dữ liệu offline | Đã triển khai phương án A; còn các giới hạn dữ liệu trên thiết bị nêu trên. |
| 8 — tenant thiếu/ngữ cảnh hệ thống | Đã fail closed; có scope hệ thống rõ, kiểm tra truy vấn cached/ignored/raw và ghi detached. |
| 9 — menu và quyền | CRUD menu đã tách khỏi cấp quyền; form cũ chứa trường cấp quyền bị từ chối. |
| 10 — audit bền vững | Source/test đạt. Cần bảng của migration 80 khi triển khai bản này. |
| 11 — bảo vệ tài nguyên/callback | Source/test đạt trong phạm vi đã chốt; ngưỡng dung lượng outbox Android còn R3. |
| 12 — CSP/thư viện | Source/test đạt; audit dependency và checksum được kiểm tra lại trong lượt này. |
| 13 — .NET | Source đã nâng .NET 10; gói local đã kiểm tra. Hạ tầng đích cần runtime/Hosting Bundle phù hợp. |
| 14 — backup/khóa/phục hồi | **Chưa nghiệm thu đầy đủ trên hạ tầng vận hành.** |

## Đánh giá các lớp còn lại

| Nhóm | Kết quả rà soát |
|---|---|
| Đăng nhập, phân quyền, CSRF | Cookie có bảo vệ, stamp và thiết bị được kiểm tra lại; mutation MVC có antiforgery mặc định, ngoại lệ tích hợp có xác thực riêng. Không tìm thêm đường nâng quyền đã xác nhận trong phần review này. |
| Cách ly cửa hàng | Có filter và interceptor kiểm tra thực thi, guard trước ghi, worker chọn từng store; không tìm thêm đường truy cập chéo cửa hàng đã xác nhận. IgnoreQueryFilters cần tiếp tục review khi thêm truy vấn vì scope guard không tự thêm điều kiện vào truy vấn đã chủ động bỏ filter. |
| SignalR | Kiểm tra session/quyền hiện hành; tenant lấy từ phiên server đã xác minh; dữ liệu tài chính lấy từ server. |
| Tiền, kho, giao hàng | Hồi quy toàn hệ thống đã đạt; atomic payment/finalize, idempotency, rowversion và rollback có bằng chứng trong báo cáo sửa. Không suy ra từ tổng test rằng mọi tình huống vận hành đều được bao phủ. |
| SQL, XML, HTTP ra ngoài | Các điểm nhạy cảm đã xem dùng tham số, XML cấm DTD/tắt resolver/có trần dung lượng; ACB/Viettel chặn URL ngoài danh sách và redirect. Chưa xác nhận đường SQL injection, XXE hoặc SSRF mới. |
| Upload/tệp riêng | Có allowlist, kiểm chữ ký media, phân tách tệp công khai/chứng từ, chặn thoát thư mục và reparse point; API chứng từ kiểm quyền và store. |
| Audit/log/secret | Snapshot lọc password/hash/token; outbox cùng transaction, retry riêng; config đang dùng không thấy mật khẩu/API key thật được gán trực tiếp trong các appsettings được quét. Các giá trị không rỗng phát hiện là connection tích hợp Windows và đường dẫn khóa; không in nội dung bí mật. Không có kiểm tra forensic toàn bộ Git history hoặc mọi log lịch sử. |
| AI và in | Token delegation ngắn hạn và recheck scope/quyền; HTTP đích được kiểm soát. LabelClient giới hạn loopback, Origin, pairing token, kích thước; kết nối AI/thiết bị thật chưa nghiệm thu trong lượt này. |
| Menu và nâng cấp | Sidebar lấy cấu hình store; nâng schema dùng `--schema-only`. Các mode `--security-seed`, bootstrap và recovery menu là thao tác riêng còn có thể tạo/reconcile menu và quyền mặc định; không dùng chúng như bước nâng cấp thông thường theo AGENTS.md. |

## Điều kiện triển khai còn thiếu bằng chứng

Kiểm tra chỉ đọc tại máy local hiện xác nhận GaoAppDb có **79 migration**, mới nhất `20261014140000_AddBankNotificationBridge`. Source bản này có migration 80 bổ sung audit. Runtime ASP.NET/.NET 10 cài toàn máy hiện là **10.0.12**, trong khi gói hiện tại yêu cầu tối thiểu **10.0.13**; các bài kiểm thử dùng SDK/runtime portable 10.0.13. Không tự cài runtime hoặc migrate GaoAppDb trong lượt rà soát.

Gói local cuối là `publish/releases/20261023-092543-2bb411b7`, chưa được triển khai lên hệ thống đang chạy. Trước nâng cấp cần xác minh đúng DB/host, chuẩn bị runtime/Hosting Bundle, backup/khóa và chạy đường schema-only được review. Cấu hình log IIS/proxy phải tránh lưu credential trong URL `/invoice-info/*`; bộ lọc log ứng dụng không kiểm soát log native/upstream. ACL, SQL service identity, TLS/proxy và phục hồi đầy đủ cần xác nhận tại môi trường đích.

## Kiểm chứng cuối lượt

| Kiểm tra mới | Kết quả |
|---|---|
| HTTP/SQL và unit bảo mật | **158/158 đạt**, 26/26 lớp có ca chạy, 0 lỗi/0 bỏ qua; thời gian 15 phút 12 giây |
| JavaScript: privacy, UI/SignalR, Android bridge, AI hạn mức | **46/46 đạt**, 0 lỗi/0 bỏ qua |
| Java: parser, companion, protocol, nhận diện, phục hồi, Retry-After | **482 assertion đạt** trong 6 bộ kiểm tra; không kết nối điện thoại thật |
| NuGet | 9 project, không có advisory được báo; gồm dependency gián tiếp |
| npm | 3 project, 0 vulnerability trong phản hồi audit, không thay package |
| Tài nguyên trình duyệt | 59/59 khớp SHA-256 |
| Source so với lần hồi quy cuối | 3.726 tệp đối chiếu, không đổi |
| CSDL sau kiểm thử | Fixture đã dọn CSDL GUID mới; 7 CSDL GaoApp có từ trước còn ONLINE |

Kết quả và kiểm tra điều kiện hoàn tất được lưu trong `.artifacts/security-recheck-20261023/verification.json`, TRX `test-results/security-recheck.trx`, log Java/Node, audit NuGet/npm, `inventory.json`, `browser-asset-integrity.json`, `android-apk-badging.txt`, `database-readonly.txt`, `database-after.txt` và `global-runtimes.txt`. Source ứng dụng không được chỉnh; báo cáo và công cụ thu bằng chứng là các tệp mới của lượt rà soát.

Bộ test xanh thể hiện các contract đã kiểm tra, không xóa những rủi ro R1–R3 hoặc thay thế kiểm thử xâm nhập/khôi phục trên hạ tầng thật. Không có kết luận forensic rằng hệ thống đã bị tấn công hoặc đã thất thoát dữ liệu.

Thứ tự đề xuất thảo luận: **R1 (phần 3 cũ) → R2 → R3 → nghiệm thu phần 14 và triển khai**. Mỗi phần trình phương án cụ thể trước khi code theo quy trình người dùng yêu cầu.
