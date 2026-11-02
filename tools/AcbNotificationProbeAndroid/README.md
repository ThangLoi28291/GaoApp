# GaoApp ACB Probe — đọc thông báo và kết nối web

APK đọc thông báo **ACB ONE** trên Android, lưu tại điện thoại. Các bản 0.1.x chỉ quan sát. Bản 0.2.0 thêm gửi thông báo mới lên web khi người dùng nhập cấu hình thiết bị; web ghép khoản chờ và ghi nhận qua luồng thanh toán hiện có. Tự xác nhận chuyển khoản chỉ khi khớp POS và đúng số tiền; chốt đơn vẫn chỉ thực hiện khi đã đủ tổng tiền.

## Sửa nhận diện và phục hồi nền — 0.2.5

Cài đè [GaoApp-AcbProbe-bridge-0.2.5.apk](../../.artifacts/acb-android-probe/GaoApp-AcbProbe-bridge-0.2.5.apk),
versionCode **12**, cùng chứng thư với 0.2.4. Giữ cấu hình, liên kết và hàng đợi đang có; không gỡ app.
SHA-256 APK: `B7E5D7027D95E9E02847921924D38E21DDC10B2DC0C62A1DBA1B79F89F7575E9`.

- Số tiền phải gắn với hậu tố `VND`: lời nhắn chứa số điện thoại, tài khoản hoặc ngày sau dấu gạch không còn bị nhầm thành tiền âm. Mẫu 167.000đ trong báo cáo đọc được và đủ điều kiện gửi khi là thông báo mới.
- Android và Web dùng 42 mẫu kiểm thử chung cho tiền nguyên VND, Unicode, POS viết liền/có dấu gạch, QR bị cắt hoặc đứng riêng, trường thiếu, OTP và nội dung bị che. Đọc lại 73 bản báo cáo được 55 bản tiền vào, khớp Web. POS/QR mâu thuẫn không được đoán để xác nhận.
- Khi xem/xuất lịch sử, app phân tích lại raw đang giữ bằng bộ đọc mới; giữ nguyên thời gian quan sát, lượt nhận, cơ sở dữ liệu và hàng đợi. Không tự gửi lại lịch sử. Báo cáo ghi `analysisKindAtCapture` và `analysisRecomputed` để phân biệt kết quả cũ.
- Bổ sung lịch đánh thức dự phòng độc lập, thử phục hồi khi có mạng/mở khóa/thoát idle, kéo lịch gửi dự phòng lên sớm khi có tin mới. Dịch vụ nối lại listener chỉ khi biết đã ngắt; không mở lại một listener còn kết nối.
- Yêu cầu mạng có hạn thời gian độc lập; thông báo tới trong lúc đang gửi được gom thành một lượt tiếp theo. Heartbeat, gửi tiền và xác nhận ACK vẫn tách riêng. Ngừng gửi hoặc 401/403 vẫn dừng; watchdog không tự bật lại.
- Báo cáo thêm dấu thời gian callback, chu kỳ gửi, heartbeat, phục hồi và hết hạn mạng, không xuất token. Không thay API xác nhận QR, ghép khoản thu, tự chốt, in bill hay mở két.

Sau khi cài, mở **Kết nối web → Bật gửi / Thử gửi lại**. Giữ quyền thông báo, **Cho phép chạy khi khóa màn hình**, và trên OPPO kiểm tra **Cho phép hoạt động nền / Tự khởi động** trong cài đặt ứng dụng. Để điện thoại khóa 20–30 phút, theo dõi `/admin/bank-notifications` trên máy tính trước khi mở điện thoại. Nếu còn mất liên lạc, xuất báo cáo mới sau lần đó để phân biệt Android giao callback trễ với hàng đợi/mạng bị kẹt.

Lịch watchdog được yêu cầu sau hai phút nhưng Android quyết định thời điểm thực thi; không phải cam kết phục hồi trong hai phút. [Doze](https://developer.android.com/training/monitoring-device-state/doze-standby),
[lịch inexact khi idle](https://developer.android.com/reference/android/app/AlarmManager#setAndAllowWhileIdle(int,long,android.app.PendingIntent))
và [quota JobScheduler Android 16](https://developer.android.com/about/versions/16/behavior-changes-all#job-quota)
có thể trì hoãn tác vụ. Chưa nghiệm thu khóa màn hình lâu trên OPPO thật; không dùng ADB và không nới trạng thái online của Web để che lỗi.

Kiểm tra: 236 assertions parser, 51 checks upload, 42 mẫu chung/140 checks Java, 32 checks phục hồi nền, 14 checks đồng hành; APK v2/v3 và căn chỉnh đạt, chữ ký trùng 0.2.4. Web: 95 kiểm thử parser, 419 kiểm thử hồi quy ACB và 239 kiểm thử UI đạt; kiểm thử trình duyệt in tiền mặt, chính sách mở két và chống in lặp đạt với Helper giả lập. Source Web có cập nhật bộ nhận diện; API gửi của APK giữ tương thích với Web hiện tại, không cần migration để thử APK này. Người dùng tự publish Web khi lấy phần đồng bộ mới.

## Gửi khi khóa màn hình — 0.2.4

Cài đè [GaoApp-AcbProbe-bridge-0.2.4.apk](../../.artifacts/acb-android-probe/GaoApp-AcbProbe-bridge-0.2.4.apk)
(versionCode 11, cùng chữ ký 0.2.3); **không gỡ app**, không xóa hàng đợi hoặc tạo lại thiết bị.
Mở **Kết nối web → Cho phép chạy khi khóa màn hình**, chấp thuận ngoại lệ pin;
bật **Cho phép thông báo chạy nền**, rồi **Bật gửi / Thử gửi lại**. Trên OPPO,
mở **Cài đặt ứng dụng / Tự khởi động**, cho phép hoạt động nền và tự khởi động nếu
máy cung cấp các mục đó. Kiểm tra trạng thái ghi **Dịch vụ nền: đang chạy** và
**Pin: đã cho phép chạy khi khóa máy**, có thông báo GaoApp trong thanh thông báo.
Có thể khóa màn hình; không cần kết nối máy tính. Chế độ liên tục dùng thêm pin,
phù hợp điện thoại tại quầy có nguồn sạc.

Thay đổi Android:
- Foreground service `specialUse` khai báo đúng mục đích relay thông báo người dùng bật,
  có thông báo riêng tư và nút Ngừng gửi; không hiện nội dung ngân hàng hoặc khóa API.
- Giữ CPU bằng lease wake-lock 180 giây khi relay chạy, gia hạn mỗi chu kỳ và nhả khi
  dừng; nếu worker bị treo thì lease tự hết. Không dùng alarm đánh thức liên tục.
- Khôi phục khi hệ thống khởi động lại tiến trình, sau boot đã mở khóa lần đầu hoặc
  cập nhật APK; tác vụ JobScheduler dự phòng còn tồn tại khi hàng đợi trống.
- Mạng trở lại kích hoạt gửi. Lỗi DB/tác vụ/ghi chẩn đoán không làm chết lịch sau đó;
  heartbeat dùng đồng hồ monotonic, chạy trước batch tối đa 2 upload để không bị
  hàng đợi dài chặn nhịp báo kết nối. Listener xin rebind khi Android ngắt, giữ luồng ghép cũ.
- Tắt gửi hoặc 401/403 dừng service và worker, giữ toàn bộ tin chưa được ACK trong SQLite.

Web: upload được xác thực cũng cập nhật liên lạc (kể cả gửi lại/chưa nhận diện), không
suy đoán queue đã rỗng. Sau restart Web vẫn dùng mốc liên lạc đã lưu. Quá 90 giây
vẫn báo Mất liên lạc; không nới thời gian hoặc coi heartbeat là xác nhận tiền.
Publish Web mới, không cần migration. Chỉ publish Web không sửa được vòng đời APK cũ.

Kiểm chứng: APK compile/resources/dex/chữ ký v2/v3/căn chỉnh đạt; 156 parser,
14 companion, 34 upload protocol và 11 lifecycle checks; 328 kiểm thử .NET thông báo/
thanh toán đạt. Không dùng ADB, không cài lên điện thoại thật hoặc giả lập force-Doze.
**Chưa nghiệm thu khóa máy dài trên OPPO thật**; cần kiểm tra sau khi cài và cấp quyền.
Android có thể hoãn mạng nếu chưa bỏ tối ưu pin, OEM vẫn có thể hạn chế nền; sau
Buộc dừng người dùng phải mở ứng dụng lại. Không cam kết vượt quyết định dừng của hệ thống.

Nguồn Android: [Doze và ngoại lệ pin](https://developer.android.com/training/monitoring-device-state/doze-standby),
[foreground service specialUse](https://developer.android.com/develop/background-work/services/fgs/service-types#special-use),
[quy định start nền](https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start).
Không dùng dataSync chạy cả ngày để tránh giới hạn 6 giờ/24 giờ, không giả dạng thiết bị Bluetooth.

## Sửa POS và QR viết liền — 0.2.3

Một số nội dung nhận từ ngân hàng có dạng `POS1838418QR1838418{32 ký tự hex}`,
không còn dấu gạch ngang. Bộ đọc Android và web khôi phục dấu phân cách để
nhận `POS-1838418` và mã QR đầy đủ. Chỉ tách chuỗi ghép khi mã QR đủ 32 ký tự
hex và số đơn trong POS/QR giống nhau; giữ kiểm tra mã bị cắt, nhiều mã khác nhau,
đúng tài khoản, đúng QR đang chờ, đúng số tiền và chống ghi nhận trùng.

Cài đè `.artifacts/acb-android-probe/GaoApp-AcbProbe-bridge-0.2.3.apk`
(versionCode 10, cùng chữ ký với 0.2.2), giữ cấu hình và dữ liệu. Nếu thông báo
cũ còn trên thanh thông báo, bấm **Đọc thông báo ACB đang hiện** để cập nhật
phần phân tích trên điện thoại. Quét lại không gửi lại giao dịch cũ lên web.
Publish bản web mới để sửa bộ đọc trên máy chủ; không cần migration. Các khoản
đã được web lưu bằng bộ đọc cũ tiếp tục đối soát/xác nhận dự phòng theo quy trình
hiện tại, không tự ghi tiền lại khi nâng cấp parser.

## Tính năng

- Trạng thái quyền truy cập thông báo, kết nối dịch vụ và ứng dụng ACB đang cài.
- Lọc đúng package `mobile.acb.com.vn`; ghi phiên bản và SHA-256 chứng thư của ứng dụng phát thông báo. Package/chứng thư được quan sát, **chưa được xác minh là chữ ký chính thức của ngân hàng**.
- Đọc tiêu đề, text, big text, text lines, ticker và nội dung MessagingStyle mà Android cung cấp. Với thông báo chưa đủ trường, thử lấy chữ từ RemoteViews đi kèm bằng API công khai; không bấm vào thông báo hoặc thực thi PendingIntent. Không OCR, không đọc màn hình, SMS, danh bạ hoặc thông tin đăng nhập.
- Hiển thị số tiền, mã đơn POS, mã FT/MGD, số tài khoản nếu có và chuỗi thời gian trong nội dung. Giờ Android đăng thông báo và giờ app quan sát được lưu riêng.
- Thiếu trường, nhiều số tiền/mã đơn, nội dung bị che hoặc bị cắt đều được ghi rõ. Không suy ra tài khoản nhận từ người chuyển hoặc từ số tiền.
- Khi Android trả trường dài đúng 1024 hoặc 5120 ký tự, app cảnh báo **có thể đã bị Android cắt**; tách dấu hiệu này khỏi việc app tự giới hạn độ dài. Không khẳng định bản gốc đầy đủ chỉ vì big text có dữ liệu.
- Lưu SQLite riêng trong app, tối đa 200 bản nội dung. Các lượt nhận/quét cùng một nội dung được đếm; đây **không phải** cơ chế chống trùng khoản thu.
- Bỏ qua nội dung OTP/SafeKey trước khi lưu và bỏ qua thông báo tổng hợp nhóm. Từ bản 0.1.1, giữ cả thông báo ACB chưa nhận diện với loại `UNRECOGNIZED`, không coi đó là tiền vào. Chỉ quan sát đúng package ACB; không lưu thông báo của ứng dụng khác. Tắt Android backup.
- Xuất JSON bằng trình chọn nơi lưu của Android. Mặc định bỏ nội dung gốc và che tài khoản; người dùng có thể chọn kèm nội dung gốc khi cần chẩn đoán.
- Mẫu minh họa trong app được gắn nhãn và không lưu vào lịch sử quan sát.

Các bản 0.1.x không khai báo quyền `INTERNET`. Bản 0.2.0 dùng quyền này cho cầu nối HTTPS do người dùng cấu hình; mặc định gửi web tắt. Thông báo thật của ACB vẫn cần điện thoại có mạng để nhận từ ngân hàng. Nút quét chỉ đọc các thông báo còn đang hiện, không đọc toàn bộ lịch sử đã xóa.

## Cài và thử trên điện thoại

1. Cài ACB ONE từ [Google Play chính thức](https://play.google.com/store/apps/details?id=mobile.acb.com.vn). Người dùng tự đăng nhập tài khoản chỉ nhận thông báo theo chức năng chia sẻ của ACB nếu đã đăng ký.
2. Chép và cài `.artifacts/acb-android-probe/GaoApp-AcbProbe-step1-0.1.5.apk`. Bản này có versionCode 6, ký bằng cùng khóa thử nghiệm với các bản trước; cài **cập nhật** để giữ dữ liệu cũ. Cho phép nguồn cài APK khi Android yêu cầu.
3. Mở **GaoApp ACB Probe → Cấp quyền đọc thông báo**, bật quyền cho app. Nếu Android chặn quyền vì APK cài ngoài: vào thông tin ứng dụng, mở menu và chọn **Cho phép cài đặt bị hạn chế** nếu thiết bị có tùy chọn này; sau đó bật lại quyền truy cập thông báo.
4. Chờ màn hình hiện **Quyền đã bật · Dịch vụ đang kết nối**. Dùng **Kết nối lại** nếu cần. Mở ACB ONE để kiểm tra tài khoản đang nhận thông báo.
5. Chọn kịch bản thử trước mỗi lượt. Người dùng tự thực hiện giao dịch nhỏ hoặc chờ giao dịch thật đến tài khoản đã đăng ký; đối chiếu từng thông báo với ACB.
6. Chạm bản ghi để xem nội dung Android trả về và các trường còn thiếu. Xuất báo cáo sau mỗi nhóm thử.

Android có thể che nội dung nhạy cảm; app không tìm cách vượt cơ chế đó. Ép dừng app bằng **Force Stop**, thu hồi quyền hoặc hạn chế nền có thể ngừng nhận thông báo. Khi đó mở lại app và kiểm tra trạng thái kết nối. Những thông báo phát sinh trong lúc bị ngừng không được cam kết phục hồi.

## Thử bằng máy ảo

Có thể dùng Android Emulator có Google Play. Cần kiểm tra thực tế ACB ONE có cho phép tài khoản nhận thông báo đăng nhập trên máy ảo không; kiểm thử bằng dữ liệu mẫu không chứng minh ACB ONE thật hoạt động trên máy ảo.

`StartEmulator.ps1` tạo AVD riêng `GaoApp_AcbProbe`, mặc định cổng `5560`, dữ liệu ở `.artifacts/acb-android-probe/avd`. Nó không dùng dữ liệu của các máy ảo đang có.

Khi cần QA dữ liệu mẫu trong lúc AVD mặc định đã cài ACB thật, dùng `-AvdName GaoApp_AcbProbe_QA_011 -Port 5562` để tạo **profile dữ liệu khác**. Chỉ đổi port không tạo profile riêng. Bộ thử từ chối cài nguồn giả lên ứng dụng ngân hàng thật.

```powershell
# Mở máy ảo có cửa sổ để người dùng thao tác Google Play / ACB ONE.
& .\tools\AcbNotificationProbeAndroid\StartEmulator.ps1 -ShowWindow

$taskAdb = 'C:\Users\wind\AppData\Local\Android\Sdk\platform-tools\adb.exe'
& $taskAdb -s emulator-5560 install -r '.\.artifacts\acb-android-probe\GaoApp-AcbProbe-step1.apk'
& $taskAdb -s emulator-5560 shell am start -a android.intent.action.VIEW -d 'market://details?id=mobile.acb.com.vn' -p com.android.vending
```

Nếu AVD đang chạy ở chế độ không có cửa sổ, dừng **đúng máy ảo này** rồi chạy lại với `-ShowWindow`. Người dùng tự đăng nhập Google Play và tài khoản nhận thông báo ACB. Không tải APK ngân hàng từ nguồn không chính thức.

## Nghiệm thu thông báo ACB thật

| Kịch bản | Quan sát cần ghi |
|---|---|
| Bình thường | Số tiền, nội dung, FT/MGD, tài khoản nếu có; thời gian nhận so với thông báo ACB |
| Khóa màn hình | Có nhận và lưu đủ nội dung khi màn hình khóa không |
| Nội dung dài | Big text / text lines có đầy đủ không; mã đơn hoặc mã giao dịch có bị cắt không |
| Nhiều giao dịch liên tiếp | Đủ từng thông báo; thông báo tổng hợp không bị nhầm thành một giao dịch |
| Khởi động lại điện thoại | Lịch sử cũ còn; dịch vụ kết nối và nhận được thông báo mới |
| Mất kết nối mạng | Dữ liệu đã lưu vẫn xem được; ghi nhận rõ ngân hàng có gửi lại thông báo khi có mạng không |
| Thông báo bị che | App báo thiếu dữ liệu; không tự suy đoán các trường |

Chỉ kết luận dữ liệu đủ cho bước đối chiếu sau khi đã thử bằng **ACB ONE thật và tài khoản nhận thông báo của người dùng**. Mã `POS-…` chỉ xác định đơn; chưa phân biệt các lần tạo QR trên cùng đơn. MGD có thể là mã nội bộ của một luồng; chưa coi là mã giao dịch duy nhất toàn ngân hàng.

## Build và kiểm thử

Ứng dụng Java thuần, dùng Android SDK/JDK có sẵn, không tải dependency hoặc cần Gradle. Mã nguồn nằm tại `app/src/main`. Bản này dùng khóa debug, dành cho bước thử nghiệm; không dùng khóa này cho bản sản xuất.

```powershell
& .\tools\AcbNotificationProbeAndroid\Build.ps1
# Nếu SDK/JDK ở nơi khác:
& .\tools\AcbNotificationProbeAndroid\Build.ps1 -AndroidSdk 'C:\path\Android\Sdk' -JavaHome 'C:\path\jdk'
```

Build chạy kiểm thử parser, compile Java/resource/dex, ký APK, kiểm tra chữ ký/căn chỉnh và xác nhận APK không có các quyền mạng/SMS/danh bạ/camera. APK và SHA-256 nằm trong `.artifacts/acb-android-probe`. Khi công cụ native Android bị sandbox chặn, cần chạy build bằng quyền thực thi phù hợp, không sửa SDK.

Các test parser sử dụng mẫu đã thay tên khách hàng, tài khoản và FT. Bộ thử emulator dùng một **nguồn phát giả lập có nhãn TEST**, package trùng ACB chỉ để kiểm tra pipeline Android trong AVD riêng trống. APK này tên `TEST-ONLY-NOT-ACB-ONE.apk`, không phải ACB ONE và không được bàn giao/cài cho người dùng. Script từ chối ghi đè app ngân hàng đã có; khi tái sử dụng hoặc gỡ nguồn giả, nó đối chiếu SHA-256 với APK fixture của chính phiên thử.

```powershell
& .\tools\AcbNotificationProbeAndroid\tests\BuildFixture.ps1
& .\tools\AcbNotificationProbeAndroid\tests\TestEmulator.ps1
# Máy ảo được reboot ở cuối lượt. Khi sys.boot_completed là 1:
& .\tools\AcbNotificationProbeAndroid\tests\FinishEmulatorTest.ps1
```

Không chạy fixture trên thiết bị thật. Sau thử, `FinishEmulatorTest.ps1` gỡ nguồn giả đã đối chiếu SHA-256, xóa dữ liệu mẫu của probe nếu nghiệm thu đạt, rồi để AVD sẵn sàng cài ACB ONE thật. Báo cáo và ảnh QA ở `.artifacts/acb-android-probe`; không đưa dữ liệu tài khoản thật vào Git.

## Nguồn kỹ thuật

- [Android NotificationListenerService](https://developer.android.com/reference/android/service/notification/NotificationListenerService)
- [Android 15: OTP redaction](https://developer.android.com/about/versions/15/behavior-changes-all#otp-redaction)
- [Android Storage Access Framework](https://developer.android.com/training/data-storage/shared/documents-files)
- [Android RemoteViews.apply](https://developer.android.com/reference/android/widget/RemoteViews#apply(android.content.Context,%20android.view.ViewGroup))
- [Android MessagingStyle.Message.getMessagesFromBundleArray, API 30 trở lên](https://developer.android.com/reference/android/app/Notification.MessagingStyle.Message#getMessagesFromBundleArray(android.os.Parcelable[]))
- [Google: restricted settings](https://support.google.com/android/answer/12623953)

Các thay đổi lịch sử của bước 1 không sửa web/menu/dữ liệu thanh toán. Bước kết nối web 0.2.0 được mô tả ở cuối tài liệu, giữ nguyên cách người dùng quản lý menu.

## Kết nối web 0.2.0 theo quy trình đã xác nhận

- Web tạo khoản chờ của đúng cửa hàng, quầy, ca, đơn, lần QR và tài khoản; lưu số tiền yêu cầu và dấu vết nội dung giỏ. Chỉ áp dụng QR LocalEmvQr/Manual có thiết bị đã đăng ký.
- Nhận `POS1838272` hoặc `POS-1838272`; **không bắt buộc QrRequestId** để tự xác nhận. Số tiền phải bằng khoản CK đang chờ, không thay bằng tổng đơn hoặc số dư còn lại. Mã QR đầy đủ nếu có được kiểm tra thêm.
- Thiếu POS thì gợi ý theo đúng tiền và thời điểm chờ tại các quầy phù hợp, kèm tên người chuyển và giờ. Chọn **Đúng khách này — Xác nhận** để ghi tiền; **Bỏ qua** chỉ ẩn gợi ý của khoản chờ đó, giữ giao dịch và cách xác nhận thủ công hiện có. Có nhiều quầy cùng số tiền thì chỉ một quầy được sử dụng khoản tiền, kiểm tra trên máy chủ.
- Xác nhận giao dịch và chốt đơn là hai việc riêng. Ghi đủ số tiền chuyển vào đơn; còn thiếu thì đơn vẫn mở, đủ tổng thanh toán thì đi qua luồng chốt/in hiện có. Không tạo hóa đơn hoặc chốt khi chỉ nhận một phần.
- Cùng giao dịch gửi lại được nhận một lần theo tài khoản và mã GD/FT, hoặc MGD theo ngày; xác nhận và gắn chứng cứ vào khoản thu trong cùng transaction. Dùng khóa SQL theo cửa hàng và đơn. Giữ nguồn `NotificationAuto`/`NotificationCashier`, không gắn nguồn callback ngân hàng cho dữ liệu từ điện thoại.
- Cấu hình điện thoại gắn đúng tài khoản ACB mà điện thoại thực sự nhận thông báo. Không suy ra tài khoản từ ngân hàng/tên người chuyển. Nếu điện thoại nhận nhiều tài khoản và thông báo không nêu tài khoản thì không dùng chung cấu hình này để tự phân loại. Thay tài khoản nhận phải đăng ký lại; dấu vết tài khoản tại lúc chờ và lúc đăng ký được kiểm tra.
- Điện thoại có hàng đợi SQLite riêng, gửi HTTPS, kiểm tra chứng thư TLS hệ thống, không theo redirect. Chỉ xếp thông báo CREDIT mới từ thời điểm nhập cấu hình, bỏ OTP/SafeKey/redacted/nội dung bị cắt; không gửi lại toàn bộ lịch sử thử. Giữ hàng đợi khi mất mạng; báo lỗi cấu hình/từ chối rõ, không gửi token trong báo cáo JSON.

Đường dẫn quản lý: **`/admin/bank-notifications`**, sử dụng quyền xem/quản lý tài khoản ngân hàng hiện có. Người dùng tự tạo menu nếu cần; không thêm menu qua seed, migration hoặc sidebar. POS sử dụng quyền tạo thanh toán; xác nhận vẫn cần quyền chốt đơn hiện có. API thiết bị chỉ tiếp nhận thông báo, không có quyền chốt/sửa đơn.

Web cần build/cập nhật cùng migration `20261014140000_AddBankNotificationBridge` trước khi ghép cấu hình điện thoại. Migration thêm bốn bảng riêng và hai cột chứng cứ nullable vào QR; không seed menu, thiết bị hoặc thanh toán. Mã nguồn và script đã được kiểm tra trên cơ sở dữ liệu SQL riêng, **chưa cập nhật IIS/database cửa hàng**. Tài khoản thực thi hiện tại không đọc được cấu hình IIS, cần xác định nơi triển khai trước khi cập nhật website đang dùng.

APK `GaoApp-AcbProbe-bridge-0.2.0.apk`, versionCode 7, cùng khóa ký các bản 0.1.x. Cài cập nhật, giữ lịch sử và liên kết đồng hành. Trên web tải cấu hình rồi trên điện thoại mở **Kết nối web → Nhập cấu hình web**. Giữ file cấu hình riêng; file cấp quyền gửi thông báo cho thiết bị. Không cần ADB. Chưa thử luồng điện thoại thật → website đang dùng → quầy sau khi triển khai; không dùng kết quả build/unit test thay cho nghiệm thu đó.

## Kết quả ngày 14/10/2026

- Đã build và cài APK `0.1.0-step1`, Android tối thiểu 8 / API 26, target API 35. Chữ ký APK v2/v3 và căn chỉnh ZIP hợp lệ.
- **62 kiểm tra parser đạt**, dùng mẫu đã thay dữ liệu cá nhân.
- **18 kiểm tra emulator đạt** trên AVD riêng Android 17 / API 37: pipeline NotificationListener, tiền vào/ra, trường thiếu, mơ hồ, OTP, nhóm, burst, nội dung dài, khóa màn hình, dữ liệu cục bộ khi mất mạng, lưu lịch sử và nhận thông báo mới sau reboot.
- Máy ảo này trả `text` và `bigText` dài tối đa 1024 ký tự cho các mẫu dài. APK cảnh báo nghi ngờ Android cắt nội dung; đây là quan sát của máy ảo đã thử, không khẳng định giới hạn của mọi điện thoại hoặc ACB ONE thật.
- Nguồn thông báo giả đã được gỡ sau khi đối chiếu SHA-256; dữ liệu mẫu trong app đã được xóa. Bằng chứng thử nằm tại `.artifacts/acb-android-probe/emulator-acceptance.json` và các snapshot SQLite chỉ chứa mẫu.
- Đã mở AVD có cửa sổ `Android Emulator - GaoApp_AcbProbe:5560` và mở Google Play tới ACB ONE. Google Play đang yêu cầu người dùng đăng nhập. **Chưa cài/đăng nhập ACB ONE thật, chưa nghiệm thu thông báo ngân hàng thật.**
- Để hoàn tất nghiệm thu bước 1: người dùng tự đăng nhập Google Play, cài ACB ONE chính thức và tài khoản chỉ nhận thông báo; bật quyền cho GaoApp ACB Probe, thử các kịch bản trong bảng và xuất báo cáo.

### Theo dõi lỗi đăng nhập ACB ONE trên máy ảo

Sau phần thử mẫu, người dùng đã cài ACB ONE **3.58.0 / 3580008** từ Google Play vào AVD. Bản ngân hàng dùng `arm64-v8a`, còn AVD chạy `x86_64` qua `libndk_translation.so`.

Log ghi nhận nhiều lần app kết thúc với `reason=2 (SIGNALED), status=31` ngay sau lỗi của lớp dịch `berberis`: `Unsupported guest syscall number in PR_SET_SECCOMP: -37377123`. Điều này chỉ ra vấn đề tương thích với yêu cầu seccomp của thư viện ARM trong môi trường dịch; chưa đủ bằng chứng để khẳng định ngân hàng chủ động cấm mọi máy ảo. Bằng chứng tối thiểu đã lọc tại `.artifacts/acb-android-probe/acb-login-exit-evidence.txt`, không chứa thông tin đăng nhập.

Cấu hình camera ban đầu cũng gây lỗi CameraX do hệ thống không có camera. Đã sửa `StartEmulator.ps1` và AVD riêng sang **hai camera emulated**, không sử dụng webcam của máy tính. Sau restart, hệ thống nhận đủ hai camera và ACB ONE vẫn chạy sau 25 giây mở app. Tuy nhiên, người dùng thử lại nút Đăng nhập vẫn bị thoát. Log mới lúc 13:32:26 và 13:33:41 (Asia/Bangkok) tiếp tục ghi nhận cùng lỗi `PR_SET_SECCOMP` từ `berberis`. Sửa camera không giải quyết lỗi tương thích của lớp dịch ARM; chưa nghiệm thu được đăng nhập hoặc thông báo ngân hàng thật trên AVD này.

Không xóa dữ liệu ACB/Google Play, không thay APK ngân hàng hoặc cấu hình bảo vệ của Android. Nếu lỗi seccomp tiếp tục xuất hiện ở bước đăng nhập, ưu tiên thử trên điện thoại Android ARM64 thật với tài khoản chỉ nhận thông báo. APK GaoApp ACB Probe vẫn dùng được cho bước quan sát trên điện thoại; máy ảo vẫn phù hợp cho các ca dữ liệu mẫu.

### Sửa thông báo đã nhận nhưng bị bộ lọc bỏ qua — 0.1.1

Ảnh điện thoại thật cho thấy bản 0.1.0 đã quan sát ACB, bộ đếm `otherIgnored=4`, nhưng chưa lưu nội dung, trong khi thanh thông báo có khoản `+8.000 VND`. Các bản bị bỏ qua không giữ dữ liệu gốc, nên ảnh chưa đủ để kết luận định dạng mà Android đã cấp cho app. Bộ lọc ban đầu đã bỏ thông báo chưa nhận diện quá sớm, làm thiếu bằng chứng chẩn đoán.

Bản 0.1.1 giữ các thông báo ACB chưa nhận diện, đọc thêm ticker/MessagingStyle/RemoteViews công khai, và lưu tên các extra, kiểu bố cục, lỗi đọc trường (chỉ tên lớp lỗi). Các trường và bố cục có dấu hiệu bị Android che không được dùng để phục hồi nội dung riêng tư. OTP/SafeKey kể cả ở RemoteViews vẫn bị bỏ trước khi lưu. Parser hỗ trợ NBSP, khoảng trắng hẹp, ký tự toàn chiều rộng, dấu cộng/trừ Unicode và ký tự định dạng vô hình; mã POS có dấu `...` hoặc `…` không được đoán thành một mã đầy đủ.

Sau khi cập nhật, giữ thông báo ACB trên thanh thông báo rồi bấm **Đọc thông báo ACB đang hiện**. Nếu có khoản đã nhận diện, chạm xem các trường. Nếu hiện **Thông báo ACB chưa nhận diện**, chạm xem dữ liệu Android trả về hoặc xuất JSON với tùy chọn kèm nội dung gốc để chẩn đoán. Các bản 0.1.0 đã bỏ qua chỉ lấy lại được khi thông báo vẫn còn hiện; các bộ đếm cũ được giữ nguyên và gắn nhãn bộ lọc cũ.

Đã có **92 kiểm tra parser và 24 kiểm tra emulator đạt** với mẫu đã thay dữ liệu cá nhân, gồm định dạng 8.000đ trong ảnh, Unicode, POS bị cắt, khoản 8.000đ trong RemoteViews, MessagingStyle, ticker, giữ nội dung chưa nhận diện, bỏ OTP trong bố cục riêng, tôn trọng nội dung bị che và nhận tiếp sau reboot. QA dùng AVD mới `GaoApp_AcbProbe_QA_011:5562`, giữ nguyên AVD cài ACB thật và điện thoại của người dùng. Nguồn giả và dữ liệu mẫu trong app đã được xóa sau kiểm thử; bằng chứng mới tại `.artifacts/acb-android-probe/qa-0.1.1/`. Chưa nghiệm thu bản 0.1.1 với thông báo ACB thật trên điện thoại của người dùng.

APK cập nhật: `.artifacts/acb-android-probe/GaoApp-AcbProbe-step1-0.1.1.apk` (versionCode 2, cùng khóa ký với bản 0.1.0 để cài đè). SHA-256: `EF9C7B63C8D5E80CD3D7EF226FB497034F0DF463350A149E80B60862F1405B3A`.

### Báo cáo trên OPPO Android 16 — chẩn đoán nội dung bị che, 0.1.2

Ba báo cáo người dùng gửi lúc 16:12–16:15 ngày 14/10 cho thấy quyền đọc đã bật và dịch vụ kết nối, trên OPPO CPH2801 / Android 16, ACB ONE 3.58.0. Báo cáo đầu chưa lưu bản ghi. Báo cáo thứ hai có ba bản `ACTIVE_SCAN`, cùng nhãn `Bình thường`; cả `android.text` và `android.bigText` chỉ chứa **“Đã ẩn nội dung thông báo nhạy cảm”**, không có ticker, messaging hoặc RemoteViews. Báo cáo cuối bỏ nội dung gốc khi xuất, chứa cùng ba captureId với deliveryCount 3 mỗi bản; 9 lượt quét không chứng minh 9 giao dịch. Chưa có bằng chứng `POSTED` trong các bản này nên chưa nghiệm thu nhận giao dịch mới ở nền.

Nội dung cấp cho listener đã bị che trước bước phân tích. Dữ liệu phù hợp cơ chế [Android 15 OTP Redaction](https://developer.android.com/about/versions/15/behavior-changes-all#otp-redaction); chưa xác định vì sao thông báo khoản thu bị phân loại nhạy cảm trên điện thoại này. Người dùng ban đầu nói có tùy chọn nhạy cảm, nhưng ba ảnh lúc 16:31 làm rõ: quyền truy cập thông báo ở màn hình đặc biệt đã bật đủ bốn loại; nút bị mờ ở Thông tin ứng dụng → Quản lý thông báo là quyền app **tự phát** thông báo. Probe chưa khai báo POST_NOTIFICATIONS và không tự phát thông báo, nên mục này không phải quyền đang thiếu để đọc ACB. Chưa thấy tùy chọn cho phép đọc nội dung nhạy cảm trong các ảnh. Màn hình quyền đọc có mục “Tùy chỉnh phạm vi của quyền truy cập thông báo” ở cuối; cần quan sát màn hình đó trước khi hướng dẫn thêm. Không thể tạo lại số tiền/mã giao dịch từ câu thông báo đã che. Luồng hiện tại chưa đủ điều kiện cho bước đối chiếu tự động; không giả vai thiết bị đồng hành hoặc vô hiệu hóa bảo vệ toàn hệ thống.

Bản 0.1.2 nhận diện chính xác câu tiếng Việt này thành `REDACTED`, hiện cảnh báo rõ và không tách trường thanh toán từ nội dung có dấu hiệu bị che. Các bản cũ cũng hiển thị cảnh báo khi còn raw; quét lại sẽ cập nhật kết quả phân tích của cùng bản ghi, giữ thời gian quan sát đầu và số lượt quét. Xuất báo cáo lược bỏ raw giữ tài khoản chưa biết là JSON `null`, sửa lỗi `***null` ở 0.1.1. Bản này là cập nhật chẩn đoán, không khẳng định đã giải quyết cơ chế che nội dung trên OPPO.

APK: `.artifacts/acb-android-probe/GaoApp-AcbProbe-step1-0.1.2.apk`, versionCode 3, cùng khóa ký để cài đè. SHA-256: `C6C39B73018EE0E24E2919A9B0B522FB590E7C04B61B88FD88001F98B2B6A777`. **110 assertions parser và 26 kiểm tra emulator đạt**, gồm nhận diện câu bị che và giữ mọi trường thanh toán là null, trước/sau reboot. Đã xuất JSON thực tế qua giao diện và Storage Access Framework trên máy ảo: raw bị loại, tài khoản không biết giữ JSON null, tài khoản mẫu có thật được che thành `***5678`, không còn `***null`. Nguồn thông báo giả và dữ liệu mẫu trong app đã được xóa. Bằng chứng QA của bản này nằm tại `.artifacts/acb-android-probe/qa-0.1.2/`. Chưa nghiệm thu đọc nội dung thật trên OPPO sau khi bật quyền nhạy cảm riêng.

### Kết luận thử tiếp trên OPPO và lựa chọn kênh dữ liệu

Người dùng xác nhận ACB ONE đã được cho phép trong phạm vi truy cập, và thông báo tiền vào mới khi máy mở khóa vẫn bị che. Không còn bằng chứng thiếu quyền truy cập thông thường. Không thể phục hồi số tiền hoặc mã giao dịch từ chuỗi đã che, nên chưa nghiệm thu bước 1 trên OPPO Android 16 này.

Đã tìm thấy AppOp riêng `android:receive_sensitive_notifications` trong mã nguồn AOSP. Tuy nhiên **người dùng không muốn dùng ADB**, nên không thử/cấp quyền đó trên điện thoại, không giữ script cấp quyền và không tiếp tục hướng dẫn ADB cho thiết bị. Máy tính chưa thấy thiết bị thật kết nối; máy ảo QA riêng chỉ được mở rồi đóng, chưa thử thay AppOp.

Hướng tiếp theo có thể thử bằng một điện thoại thật Android 12–14 có sẵn với tài khoản chỉ nhận thông báo và cùng APK; cơ chế redaction của listener được Android công bố từ Android 15, nhưng vẫn phải kiểm tra dữ liệu thật trước khi kết luận máy khác đọc được. Theo [ACB thông báo ngày 25/09/2026](https://acb.com.vn/thong-bao/acb-thong-bao-nang-cap-he-dieu-hanh-toi-thieu-doi-voi-ung-dung-acb-one), ACB ONE yêu cầu Android tối thiểu 7.0; probe yêu cầu Android 8. Không đề nghị mua máy hoặc hạ hệ điều hành trước khi thử thực tế. Hướng triển khai lâu dài là [API Products của ACB](https://developer.acb.com.vn/acb/open/vi/product): có dịch vụ thông báo thanh toán thành công và truy vấn lịch sử giao dịch, nhưng cần xác nhận điều kiện đăng ký cho tài khoản cửa hàng này, chưa giả định tài khoản chia sẻ thông báo tự có quyền API.

## Thử thiết bị đồng hành thật, không ADB — 0.1.3

Người dùng đã yêu cầu thử phương án này, không dùng ADB. Bản 0.1.3 thêm ghép nối qua **CompanionDeviceManager** với một máy tính Bluetooth thật và kết nối RFCOMM được xác thực/mã hóa. Không tạo liên kết self-managed, không giả đồng hồ, không xin profile computer đặc quyền, không thay AppOps hoặc khai báo RECEIVE_SENSITIVE_NOTIFICATIONS. Hộp thoại của Android do người dùng chọn/xác nhận; mã ghép chứa địa chỉ của radio thật trên máy tính và mã ngẫu nhiên của phiên helper. Đây là thử một kết nối đồng hành có hoạt động thực tế, không chỉ tạo bản ghi liên kết.

Máy tính của phiên này có **Intel Wireless Bluetooth**. Helper Windows đăng ký SDP riêng của GaoApp, dùng Bluetooth RFCOMM có xác thực và mã hóa, giới hạn thông điệp ASCII và chỉ chấp nhận kiểm tra phiên/nhịp kết nối. **Không truyền nội dung thông báo, số tiền, mã đơn, tài khoản hoặc OTP sang máy tính.** Điện thoại vẫn lọc đúng ACB ONE và bỏ OTP trước khi lưu. Mã phiên không xuất vào JSON. Ghép xong, app yêu cầu Android kết nối lại listener; điều đó không bảo đảm ColorOS cấp dữ liệu đầy đủ.

### Cách thử trên điện thoại

1. Cài cập nhật `.artifacts/acb-android-probe/GaoApp-AcbProbe-step1-0.1.3.apk`, versionCode 4, cùng khóa ký với các bản trước.
2. Mở `.artifacts/acb-android-probe/GaoApp-Companion.exe` trên máy tính, bấm **Bắt đầu**. Mã `GAO1|…|…` hiện trong ô có thể chọn/copy. Helper cho phép tìm thấy radio trong 10 phút; Dừng/Bắt đầu sẽ tạo phiên/mã mới và mở lại thời gian ghép.
3. Bật **Bluetooth và Vị trí** trên OPPO. Mở Probe → **Ghép máy tính đồng hành**, nhập đủ mã từ helper và bấm **Ghép máy tính**. Khi Android hỏi, cấp quyền **Thiết bị lân cận** để app kết nối Bluetooth.
4. Chọn đúng máy tính trong hộp thoại của Android. Xác nhận ghép Bluetooth trên điện thoại và Windows nếu được hỏi; kiểm tra đúng thiết bị/mã mà hệ điều hành hiển thị.
5. Chờ điện thoại báo **Đã ghép với máy tính và kiểm tra kết nối thật**, trạng thái liên kết Android và kết nối trực tiếp đều hoạt động. Liên kết vừa tạo mà chưa kiểm tra được kết nối sẽ được thử hủy; nếu hủy thất bại, giao diện báo rõ để người dùng bấm **Hủy ghép thiết bị**.
6. Về màn hình Probe, chờ **Dịch vụ đang kết nối**, đọc lại thông báo đang hiện và thử thêm một thông báo ACB mới. Xuất JSON **Kèm nội dung gốc**. Trường `companion` trong báo cáo và bản ghi mới cho biết liên kết đã được Android chấp thuận, kết nối máy tính có được kiểm tra và đang hoạt động không. Các thông báo cũ không có metadata này cho đến khi quét lại.

Nếu khởi động lại helper, mã phiên thay đổi: nhập mã mới rồi bấm Ghép máy tính; liên kết đang có của cùng radio được dùng lại. Ngừng kết nối giữ liên kết đã kiểm tra; **Hủy ghép thiết bị** xóa liên kết do GaoApp tạo và thông tin phiên cục bộ, giữ lịch sử quan sát. Helper có nút Dừng; đóng cửa sổ sẽ xóa SDP và khôi phục trạng thái tìm thấy/kết nối mới của radio đã quan sát trước khi chạy. Không tắt Bluetooth của máy tính.

### Build và bằng chứng của bản đồng hành

```powershell
& .\tools\AcbNotificationProbeAndroid\Build.ps1
& .\tools\AcbNotificationProbeAndroid\BuildCompanionHost.ps1
& .\.artifacts\acb-android-probe\GaoApp-Companion-checks.exe --smoke
```

Đã đạt **110 kiểm tra parser, 14 kiểm tra giao thức Java, 11 kiểm tra giao thức/bố cục native C#**. Đã kiểm tra bằng radio thật: bind/listen RFCOMM, yêu cầu xác thực và mã hóa, đăng ký SDP, dừng và khôi phục trạng thái radio. Đã render và xem giao diện helper. **Không chạy ADB, không cài APK/ghép Android bằng lệnh, chưa nghiệm thu hộp thoại trên OPPO, kết nối điện thoại–PC hoặc nội dung ACB thật sau ghép.** Không áp dụng kết quả 26 kiểm tra emulator của bản 0.1.2 thành nghiệm thu 0.1.3.

SHA-256 APK 0.1.3: `0C16AF034488E2083EC492332D9FEEEDDE9D39ACE66236B692B28490EEF594B6`. Helper: `7726BABE95C3B3E29503B4792E34C9A13DAB678DDEB6DD7187ED8C57E286CC57`.

Nguồn kỹ thuật: [Android Companion device pairing](https://developer.android.com/develop/connectivity/bluetooth/companion-device-pairing), [ngoại lệ đối với ứng dụng đồng hành](https://developer.android.com/about/versions/15/behavior-changes-all#otp-redaction), [Windows Bluetooth socket options](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-socket-options), [Windows SDP service registration](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-wsaqueryset-for-set-service). Phải kiểm chứng trên OPPO trước khi kết luận giải quyết được việc che nội dung. Ghép Bluetooth thông thường của ứng dụng khác không tạo liên kết CompanionDeviceManager cho GaoApp.

## Báo cáo sau ghép thành công — sửa vòng đời listener, 0.1.4

Báo cáo người dùng xuất lúc 18:16 ngày 14/10 trên OPPO CPH2801 / Android 16, app 0.1.3, xác nhận `associationApproved`, `deviceHandshakeVerified` và `linkConnected` đều true. Kiểm tra kết nối gần nhất lúc **18:09:02**; một thông báo **POSTED mới lúc 18:13:55**, gần 293 giây sau đó, vẫn chỉ có câu **“Đã ẩn nội dung thông báo nhạy cảm”** ở text và bigText. Không có RemoteViews hay lỗi đọc trường, mọi trường thanh toán null. Phần ghép/kết nối đã được kiểm chứng trên điện thoại thật; việc nhận đầy đủ nội dung ACB chưa đạt. Các bản cũ được quét lại có metadata của lượt quan sát sau cùng, không phải metadata thời điểm nhận lần đầu.

Rà soát phát hiện 0.1.3 chỉ gọi `requestRebind()` khi listener có thể vẫn đang bound. Theo [API NotificationListenerService](https://developer.android.com/reference/android/service/notification/NotificationListenerService#requestRebind(android.content.ComponentName)), rebind được dùng sau unbind. [Mã nguồn Android 16](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/android16-release/services/core/java/com/android/server/notification/NotificationManagerService.java) đánh giá tin cậy khi bật dịch vụ hoặc thêm kết nối dịch vụ, lưu UID vào danh sách tin cậy, và ghi rõ không theo dõi thay đổi liên kết CDM tại bước kiểm tra đó. Đây là căn cứ để sửa bước làm mới, **chưa chứng minh ColorOS đang giữ cache là nguyên nhân duy nhất của báo cáo**.

Bản 0.1.4 dùng API unbind của đúng service trong app rồi rebind, đợi callback `onListenerConnected` mới để đánh dấu CONFIRMED. Dùng static unbind từ API 34, instance unbind khi listener đã connected trên các API cũ. Có timeout và hướng dẫn tắt/bật quyền qua giao diện nếu không có xác nhận; tiếp tục yêu cầu đang dở khi người dùng mở lại app, không để pending cũ chặn nút thử lại. Sau xác nhận mới, quét các thông báo đang hiện với nhãn ACTIVE_SCAN. Không xóa lịch sử, không sửa quyền hệ thống/ACB, không dùng ADB. Báo cáo thêm `listenerLifecycle`; mỗi lượt quan sát mới lưu số phiên và thời điểm callback kết nối, kể cả khi cập nhật bản trùng nội dung.

Người dùng có thể thử ngay trên 0.1.3 bằng cách giữ liên kết đồng hành, tắt rồi bật lại quyền truy cập thông báo của GaoApp qua giao diện. Tại lúc đóng gói 0.1.4 chưa nhận phản hồi. Sau đó người dùng xác nhận **“Đã đọc được nội dung ACB”** khi thực hiện bước tắt/bật quyền với helper và Bluetooth đang chạy. Đây là bằng chứng thực tế thao tác làm mới quyền sau ghép đã có tác dụng, phù hợp với cơ chế cập nhật tin cậy của Android. Chưa có JSON sau lần đọc thành công để xác nhận phiên bản APK đang dùng, trường tiền/POS/FT/MGD/tài khoản hoặc độ ổn định ở các kịch bản khác. Bản mới tự thực hiện bước khởi động lại service qua API, **không tự tắt/bật quyền do người dùng quản lý**; chưa coi thao tác tự động của 0.1.4 là đã được nghiệm thu chỉ từ kết quả tắt/bật quyền thủ công.

APK `.artifacts/acb-android-probe/GaoApp-AcbProbe-step1-0.1.4.apk`, versionCode 5, SHA-256 `2569EAAF95C31A4C39B9EBFB246E8EF901675A196C63D10CEC1259A91CEE54CB`. Helper Windows giữ nguyên SHA-256 `7726BABE95C3B3E29503B4792E34C9A13DAB678DDEB6DD7187ED8C57E286CC57`. Bộ chuyển sang máy khác: `.artifacts/acb-android-probe/GaoApp-Companion-Test-0.1.4.zip`. Build, chữ ký/căn chỉnh/quyền APK, 110 kiểm tra parser và 14 kiểm tra giao thức Java đã đạt. Chưa kiểm chứng callback khởi động lại hoặc nội dung ngân hàng thật của 0.1.4 trên OPPO; không dùng ADB cho các kiểm tra này.

## Đọc được sau ghép và khi ngắt máy tính — sửa POS liền số, 0.1.5

Ảnh người dùng gửi sau bước làm mới quyền xác nhận nội dung thật đã đọc được: tin 18:44:55 có tiền vào 313.000 VND, MGD 31017, không chứa POS hoặc FT. Ảnh tiếp theo có tiền vào 172.000 VND, MGD 31018, thời gian trong nội dung 18:54:54 và mã `POS1838268-GD-…`. Người dùng cho biết đã ngắt kết nối máy tính; app vẫn nhận được nội dung mới, hiển thị nguồn POSTED. Giờ Android đăng là 18:54:55; lần đầu quan sát là 18:56:18, vì vậy ảnh không chứng minh nhận ngay tức thì. Chưa có JSON mới xác nhận phiên bản app và trạng thái liên kết tại thời điểm nhận. Chưa nghiệm thu việc đọc sau reboot điện thoại, nhiều giao dịch liên tiếp hoặc hủy liên kết đồng hành; không suy ra luôn đọc được khi mất kết nối chỉ từ lần thử này.

Parser cũ bắt buộc dấu gạch ngang sau POS nên bỏ sót `POS1838268`, dù nội dung đã đầy đủ. Bản 0.1.5 nhận cả `POS1838268` và `POS-1838268`, chuẩn hóa trường mã đơn thành `POS-1838268`. Vẫn không chọn mã nếu có nhiều số khác nhau, mã bị cắt bằng dấu ba chấm hoặc dính chữ/số vượt giới hạn. Fixture dùng định dạng của ảnh, thay tên người gửi và mã tham chiếu; không lưu thông báo hoặc tài khoản thật vào mã nguồn.

Cài cập nhật APK, không gỡ app hay hủy liên kết đã ghép. Bấm **Đọc thông báo ACB đang hiện** để tách lại mã của tin còn hiện: bộ lưu cập nhật phần phân tích của cùng bản ghi và đếm thêm một lượt quét, không coi là khoản tiền mới. Lịch sử của tin đã xóa khỏi thanh thông báo không tự được phân tích lại trong bản này. Tiếp tục xuất JSON kèm nội dung gốc khi thử.

APK `.artifacts/acb-android-probe/GaoApp-AcbProbe-step1-0.1.5.apk`, versionCode 6, SHA-256 `FEF53749D4A4B50128AB6987F336C267D98EF6B405500E671B8FF4283AE426A2`. Bộ chuyển máy `.artifacts/acb-android-probe/GaoApp-Companion-Test-0.1.5.zip` giữ nguyên helper Windows. Build đã đạt **131 kiểm tra parser và 14 kiểm tra giao thức Java**, chữ ký APK v2/v3, căn chỉnh và kiểm tra quyền. Chưa cài/nghiệm thu bản 0.1.5 trên điện thoại thật. Không dùng ADB, không thay cơ chế ghép/đọc của 0.1.4, chưa kết nối web hoặc tự xác nhận thanh toán.

## Hai nội dung khác nhau sau khi đọc thành công, 19:04–19:05

Người dùng tiếp tục xác nhận không cần kết nối máy tính khi nhận tin. Ảnh lúc 19:05 cho thấy tin từ Shinhan: 861.000 VND, `POS-1838272`, chuỗi `QR-1838272-…` với mã hex trong nội dung, MGD 31020, giờ nội dung 19:04:32. Android đăng và app quan sát cùng lúc 19:04:34, nguồn POSTED. Ảnh lúc 19:06 cho thấy tin từ ngân hàng Công Thương: 196.000 VND, MGD 31021, giờ nội dung 19:05:38; Android đăng và app quan sát cùng lúc 19:05:40. Trong chữ Android trả về ở ảnh thứ hai không thấy POS hoặc mã QR GaoApp, chỉ có tên người chuyển, lời chuyển tiền và GD/MGD. Cả hai đều có nội dung giao dịch thay vì câu thông báo nhạy cảm bị che. Ảnh chưa xác nhận phiên bản APK, trạng thái CDM hoặc toàn bộ trường thông báo; không kết luận một ngân hàng luôn giữ hoặc luôn bỏ mã chỉ từ hai tin.

Đối chiếu mã hiện có tại `GaoApp.Application/Services/POSPaymentQrs/POSPaymentQrService.cs`: QR thủ công tạo `RequestCode` theo `QR-{OrderId}-{Guid:N}`, thay vào `NoteTemplate` (mặc định `POS-{OrderId}-{QrRequestId}`), lưu cùng số tiền và tài khoản nhận trong `PosPaymentQrRequest`. Vì vậy mã QR đầy đủ là thông tin để nhận diện lần tạo QR, trong khi riêng POS chỉ nhận diện đơn. Probe hiện tách POS và FT/MGD, chưa tách riêng mã QR hoặc nối sang dữ liệu đơn của web; cảnh báo chưa xác định lần QR là giới hạn của bản quan sát hiện tại.

Định hướng cho bước đối chiếu tiếp theo, chưa triển khai: chỉ xét tự xác nhận khi có mã nhận diện đủ để khớp một khoản chờ của đúng cửa hàng/tài khoản, số tiền phù hợp, trạng thái còn hợp lệ và kiểm tra chống ghi trùng. Thiếu mã hoặc nhiều ứng viên thì lưu tiền vào chưa ghép, gợi ý đối soát cho người dùng; không chọn và xác nhận chỉ bằng số tiền/tên người gửi/thời gian. GD/MGD nhận diện chứng cứ giao dịch, không tự cung cấp mã đơn đã mất. Nếu nội dung có mã mà parser bỏ sót thì sửa parser; nếu các trường Android thực sự không chứa mã thì cần chứng cứ bổ sung từ nguồn đối soát, không suy đoán mã bị thiếu.
