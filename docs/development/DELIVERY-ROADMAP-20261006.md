# Roadmap giao hàng và giám sát thời gian thực

Ngày lập: 06/10/2026; cập nhật: 07/10/2026. Dự án: GaoApp. Phương án đã chốt: **Web cập nhật theo các mốc trước, app và GPS bổ sung sau**.

Mục tiêu đợt đầu là nối trọn luồng **POS tạo đơn giao → quét QR hoặc nhập mã để soạn → xử lý thiếu hàng → phân công và ghi nhận xuất phát → ghi nhận thực giao, hàng mang về và tiền thu → quầy bất kỳ đối soát và chốt → theo dõi hàng còn thiếu**, rồi đưa các lần cập nhật lên màn hình quản lý tại văn phòng.

**Đợt một gồm 13 bước D00 đến D12, dùng hoàn chỉnh trên máy tại tiệm, không cần app, GPS hoặc điện thoại Android để nghiệm thu.** Nhân viên cập nhật khi nhận soạn, bàn giao, xuất phát, nhận báo cáo từ người giao và khi người giao trở về. Màn hình văn phòng tự nhận thay đổi sau khi lưu; ngoài đường chỉ có thông tin theo lần báo cáo gần nhất.

**Đợt hai gồm M00 đến M03, để sau:** ứng dụng Android, GPS và kết nối vị trí vào màn hình đã có. Đợt một được kiểm tra, nghiệm thu và phát hành độc lập; các bước M chưa làm không chặn đợt một và không được ghi PASS.

Mỗi bước có sản phẩm cụ thể, test bắt buộc và cổng kiểm tra. **Test thuộc bước đang làm chưa chạy, lỗi hoặc thiếu bằng chứng thì chưa được qua bước kế tiếp trong cùng đợt.** Không lấy thời gian đã làm hoặc giao diện đã đẹp làm điều kiện hoàn thành.

Trạng thái tại thời điểm chốt nguồn tài liệu 07/10/2026: **D00 kiểm tra tự động đạt, thiết bị/in giấy còn chờ; D01–D03 có nguồn và bằng chứng local; D04 đã được Coordinator nghiệm thu toàn bộ cổng local; D05–D12 CHƯA THỰC HIỆN; M00–M03 ĐỂ SAU**. [Báo cáo D04](DELIVERY-D04-20261006.md) ghi luồng nhận soạn/báo thiếu/thay/duyệt, các kết quả thật và giới hạn của bằng chứng. Review độc lập và hai required full CI trên cùng commit cuối chưa có ở thời điểm này; [cổng cuối do Coordinator cập nhật](../../Logs/delivery-roadmap/D04/run-20261007-01/coordinator-final-gates.json) giữ trạng thái hiện hành. Chưa merge hoặc triển khai tại tiệm. Dữ liệu thật được dùng qua COPY_ONLY/clone GUID; GaoAppDb gốc không migrate hoặc sửa dữ liệu/quyền.

## 1 Phạm vi nghiệp vụ đã thống nhất

Trước D01, theo yêu cầu user, nâng cấp lại màn hình giám sát cho toàn bộ khu vực đã kết nối: nền sáng, công việc riêng từng quầy/người, mốc giờ và animation riêng. Phạm vi và bằng chứng nằm tại [STORE MONITOR UPGRADE 20261006](STORE-MONITOR-UPGRADE-20261006.md). Công việc này không mở bước giao hàng hoặc thay đổi cổng D00.

| Mã | Quy tắc bắt buộc |
| --- | --- |
| B01 | POS có lựa chọn tạo đơn giao. Tạo đơn và in bill giao không đồng nghĩa đã hoàn thành bán hàng hoặc đã thu tiền. |
| B02 | Bill giao có QR và mã đơn dễ đọc. Nhân viên quét bằng thiết bị hỗ trợ QR hoặc nhập mã trên Web để mở đúng đơn và phiên bản hiện tại, rồi nhận việc soạn. Không bắt buộc có app hoặc điện thoại để nhận soạn. |
| B03 | Soạn hàng có số lượng thực tế và số lượng thiếu theo từng mặt hàng. Quản lý thấy thiếu hàng và yêu cầu xử lý; người có quyền được giảm, bỏ hoặc thay hàng trước khi bàn giao. Mọi sửa đổi có lịch sử. |
| B04 | Đơn đã soạn được bàn giao cho nhân viên giao có trách nhiệm rõ ràng. Đợt một lưu tên người nhận, số điện thoại, địa chỉ và ghi chú đường đi; nhân viên tại tiệm bấm ghi nhận xuất phát. Chọn điểm trên bản đồ và mở Google Maps không phải điều kiện hoàn thành bản đầu. |
| B05 | Đợt một hiển thị người giao, đơn đang mang, giờ xuất phát, thời gian đã đi và lần cập nhật cuối. Vị trí GPS thực thuộc đợt hai; đợt một không hiển thị xe chạy trên bản đồ như thể đang có GPS. |
| B06 | Nhân viên có quyền tại tiệm ghi nhận giao đủ, giao một phần, không giao được, hàng mang về và tiền người giao báo đã nhận. Có thể nhập khi người giao gọi về hoặc khi trở về; lưu cả người nhập và người giao được nhập hộ. Người có quyền chốt xử lý sau đối soát. |
| B07 | Chỉ tính tiền hàng khách thực sự nhận. Hàng thiếu hoặc mang về chưa giao không tự biến thành công nợ tiền của khách. |
| B08 | Quầy bất kỳ trong cùng cửa hàng, có quyền và có ca đang mở, được chốt. Ghi tiền và trách nhiệm chốt cho quầy/ca thực hiện; vẫn giữ thông tin quầy/ca tạo ban đầu. |
| B09 | Khi đối soát đủ, chốt bằng tiền mặt, xác nhận đã nhận chuyển khoản hoặc công nợ nếu khách đủ điều kiện. Xác nhận chuyển khoản không tạo QR chuyển khoản mới. |
| B10 | Lưu danh sách hàng khách còn thiếu để theo dõi. Khi có hàng, tạo việc liên hệ khách; khách nhận bù thì có lần giao tiếp theo, hoặc đóng phần thiếu khi khách bỏ qua. |
| B11 | Màn hình văn phòng có bố cục và hiệu ứng đẹp, thể hiện sự kiện và tiến độ thật của công việc. |

Đợt một chỉ yêu cầu trình duyệt trên máy tại tiệm. Nếu cần, nhân viên có thể mở Web trên điện thoại; đó là cách truy cập bổ sung, không phải điều kiện để luồng tại tiệm hoạt động và không cam kết cập nhật GPS nền. Đợt hai ưu tiên Android, API/nghiệp vụ dùng lại được nếu sau này thêm iPhone. Không mở rộng sang tối ưu tuyến đường tự động, cổng đặt hàng công khai hoặc tự gửi tin cho khách. Việc liên hệ khách trước mắt là tác vụ cho nhân viên.

| Mốc cập nhật đợt một | Người thao tác trên Web | Màn hình văn phòng hiển thị |
| --- | --- | --- |
| Tạo đơn | Thu ngân | Đơn mới chờ soạn |
| Nhận và hoàn tất soạn | Nhân viên soạn | Người soạn, lượng thực tế, hàng thiếu |
| Bàn giao và xuất phát | Người có quyền bàn giao tại tiệm | Người giao, đơn mang theo, giờ đi |
| Người giao gọi về nếu có | Nhân viên được phép nhập hộ | Kết quả được báo, người nhập hộ, thời điểm cập nhật |
| Người giao trở về | Nhân viên nhận bàn giao | Hàng thực giao, hàng mang về, tiền bàn giao và chênh lệch |
| Đối soát và chốt | Thu ngân/quản lý có quyền | Kết quả chốt, quầy/ca chốt, hàng khách còn thiếu |

## 2 Hiện trạng cần bảo vệ

Khảo sát ngày lập tài liệu xác định các điểm sau. Phải đọc lại tại đầu mỗi bước vì mã nguồn có thể thay đổi.

| Khu vực | Hiện trạng và tác động đến thiết kế |
| --- | --- |
| [Order](../../GaoApp.Domain/Entities/Order.cs) | Đơn hiện gắn với `POSShiftId`; mã đơn bán được cấp khi chốt. Đơn giao cần mã/QR ổn định từ lúc tạo, trước khi có đơn bán hoàn thành. |
| [POSService](../../GaoApp.Application/Services/Orders/POSService.cs) | `EnsureCanBeCurrentCart`, `AddPaymentCoreAsync`, `FinalizeCoreAsync` ràng buộc giỏ hàng, ca và quầy; chốt hiện trừ kho, ghi tiền, công nợ và các dữ liệu liên quan trong transaction. Phải có đường xử lý đơn giao phù hợp, không gỡ kiểm tra của POS thông thường. |
| [POSShiftService](../../GaoApp.Application/Services/POSShifts/POSShiftService.cs) | Đơn nháp/giữ có dữ liệu có thể chặn đóng ca. Đơn giao đang xử lý không được buộc quầy tạo phải mở ca đến lúc người giao quay về. |
| [InventoryReservationService](../../GaoApp.Application/Services/Inventory/InventoryReservationService.cs) | Có giữ/giải phóng/tiêu thụ lượng giữ cho đơn; phải kiểm tra khả năng áp dụng cho soạn thiếu, nhiều đơn tranh hàng và bàn giao từng lượng. |
| [Giám sát hiện có](../store-monitor.md) | Luồng giám sát dùng bộ nhớ tạm của một Web instance. Trạng thái đơn giao và đối soát phải có nguồn lưu bền; khởi động lại không làm mất công việc đang chạy. GPS sẽ có nguồn riêng khi triển khai đợt hai. |
| [Hoàn hàng và giá vốn](POS-RETURNS-COST-READINESS-20261004.md) | Có xử lý hoàn hàng, chờ nhập lại và nguồn giá vốn. Hàng giao mang về và hàng khách trả sau khi bán phải phân biệt để tránh nhập kho/đảo giá vốn hai lần. |
| [Test HTTP thực](../../GaoApp.Tests/Security/FullApplicationFixture.cs) | Có fixture chạy Web thật, SQL test, đăng nhập và hai cửa hàng. Dùng để kiểm tra quyền, transaction và cách ly cửa hàng; không chỉ test service giả. |

Source khảo sát: branch `chore/source-checkpoint-20260925`, HEAD `418bb23aefff35cff228fc8e1cbb72b4ee8bc40b`. Working tree đang có nhiều thay đổi chưa commit, gồm giám sát và báo cáo. HEAD này **không đại diện đầy đủ cho bytes đã khảo sát**. D00 phải lập danh sách và hash nguồn thực tế, giữ nguyên thay đổi hiện hữu.

Phạm vi lượt cập nhật kế hoạch là Level A, chỉ sửa roadmap này và bổ sung quyết định tương ứng trong `docs/development/DECISION-LOG.md`, giữ nguyên các quyết định cũ. Không chạy build/test chức năng, migration, database update hoặc thay đổi Git trong lượt này. Kiểm tra tài liệu gồm đủ bước/test/cổng, đường dẫn nguồn và trạng thái chưa chạy; không gọi đó là `TASK PASS` của tính năng.

Trong PATH lúc khảo sát chỉ xác nhận được `dotnet`; chưa xác nhận JDK, Android SDK, `adb`, Gradle hoặc Flutter. Việc kiểm chứng các công cụ và APK chuyển sang M00, không phải dependency của D00 hoặc bản Web.

## 3 Các nguyên tắc kỹ thuật cần khóa

Các tên thực thể dưới đây là **đề xuất để chốt ở D01**, chưa phải tính năng đã tồn tại.

1. Có hồ sơ đơn giao riêng và liên kết rõ với đơn bán cuối cùng. Lưu dòng hàng, phiên bản, người nhận/địa chỉ tại thời điểm đặt, người tạo, quầy/ca tạo và nguồn kho. D01 quyết định dùng hồ sơ giao độc lập hay mở rộng đơn hiện có, kèm cách tương thích POS; không đổi quầy/ca gốc để lách kiểm tra chốt.
2. Tách trạng thái xử lý hàng, trạng thái đối soát tiền và theo dõi hàng thiếu. Ví dụ được báo đã giao cho khách vẫn có thể đang chờ bàn giao tiền. Đợt một lưu lần cập nhật cuối; hết thời gian dự kiến không tự suy ra đã giao xong. Độ mới GPS bổ sung độc lập ở đợt hai.
3. QR chỉ chứa định danh/token tra cứu đơn ổn định, không chứa thông tin khách nhạy cảm hoặc quyền tự chốt. Máy chủ vẫn kiểm tra đăng nhập, cửa hàng và quyền. Bill cũ mở phiên bản mới nhất và báo nếu nội dung đã đổi.
4. Số lượng phải lưu cả đơn vị bán và lượng quy đổi chuẩn. Dòng thiếu, dòng thay thế, hàng đã bàn giao, thực giao và trả về có quan hệ truy vết; không chỉ dựa vào phần trăm tiến độ.
5. Giữ hàng khi soạn; bàn giao cho người giao phải phản ánh hàng thực rời kho. Hàng mang về chỉ nhập lại sau khi người có trách nhiệm xác nhận nhận hàng. Chốt tiền không trừ kho lần nữa. Giữ đầy đủ nguồn giá vốn và legal entity, không lấy kho của quầy chốt thay kho xuất gốc.
6. Khóa giá/chiết khấu/thuế theo phiên bản được duyệt. D01 phải định nghĩa cách phân bổ giảm giá cả đơn và làm tròn khi giao một phần, cả trường hợp combo/đơn vị quy đổi. Thay hàng có thay đổi giá phải có xác nhận phù hợp; không âm thầm tính giá mới cho khách.
7. Công nợ khách chỉ là tiền của hàng đã giao mà được phép ghi nợ. Tiền người giao thu nhưng bàn giao thiếu là chênh lệch người giao; không tự ghi thêm vào nợ khách.
8. Một thao tác nghiệp vụ có khóa chống gửi trùng và kiểm soát phiên bản. Lỗi giữa chừng phải rollback cả trạng thái, tồn kho, tiền và sự kiện tương ứng. Hai người cùng chốt chỉ có một kết quả tài chính.
9. SQL là nguồn sự thật. Sự kiện giám sát được phát sau commit; cơ chế lưu sự kiện chờ phát có thể thử lại sau restart. Không phát hiệu ứng thành công cho thao tác thất bại hoặc bị từ chối.
10. Mọi quyền kiểm tra ở máy chủ, gồm phạm vi cửa hàng, kho/legal entity, người đang được giao việc và trạng thái hợp lệ. Lịch sử lưu người thực hiện, thời điểm máy chủ, số lượng/tiền trước sau và lý do sửa/ghi đè.
11. Nhập hộ phải là quyền rõ ràng. Tài khoản đang đăng nhập là người ghi nhận; người giao là người được phân công, không giả đăng nhập dưới danh tính người giao. Lưu nguồn thông tin như báo qua điện thoại hoặc bàn giao trực tiếp; nếu có giờ người giao báo thì tách khỏi giờ server ghi nhận. Báo đã thu tiền hoặc đã mang hàng về không thay thế xác nhận đã nhận tiền/hàng tại tiệm.

Luồng dự kiến để D01 kiểm tra chuyển trạng thái:

```text
Tạo đơn → Chờ soạn → Đang soạn → Chờ bàn giao → Đã xuất phát
                                                    ↓
               Nhận báo cáo nếu có → Trở về → Chờ đối soát → Đã chốt

Nhánh xử lý: thiếu hàng / cần quản lý / không giao được / hàng mang về.
Nhánh theo dõi thiếu hàng tiếp tục độc lập sau khi đơn bán đã chốt.
Hủy trước xuất kho và kết thúc chuyến không giao được là hai tình huống khác nhau.
Không cần báo cáo giữa chuyến; có thể ghi kết quả toàn bộ khi người giao trở về.
```

## 4 Cổng kiểm tra trước khi chuyển bước

### 4.1 Cổng test của từng bước

Một bước chỉ có `TEST PASS` khi **đồng thời** đáp ứng:

- Bước trước đã đủ điều kiện; phạm vi, đầu vào, đầu ra, quyền, các file được sửa và test plan của bước hiện tại đã được khóa trong Task Contract.
- Build phù hợp với thay đổi đạt; toàn bộ test bắt buộc được tìm thấy và chạy. Mỗi bộ test bắt buộc có số test lớn hơn 0; không chấp nhận lệnh thành công nhưng không tìm thấy test.
- Có 0 test thất bại và 0 test bắt buộc bị bỏ qua trong phạm vi bước/đợt hiện tại. Không có SQL hoặc chưa thử máy in khi đó là điều kiện bắt buộc phải ghi `CHƯA ĐỦ ĐIỀU KIỆN`, không ghi PASS. Điện thoại/SDK/GPS chỉ là điều kiện của các bước M; không dùng để chặn test đợt Web.
- Test kiểm tra dữ liệu sau commit và các tổng kho/tiền/công nợ/giá vốn cần thiết; HTTP 200, ảnh giao diện hoặc event xuất hiện riêng lẻ chưa đủ.
- Các lỗi ảnh hưởng đến đúng tiền, tồn kho, bảo mật, chống trùng, mất dữ liệu và chặn luồng đã được xử lý. Lỗi không chặn phải ghi rõ phạm vi và người chịu trách nhiệm; không dùng để bỏ qua tiêu chí bắt buộc.
- Lưu lệnh thực tế, exit code, số pass/fail/skip, provider SQL, phiên bản ứng dụng/thiết bị, hash nguồn và đường dẫn bằng chứng. Bằng chứng không chứa mật khẩu/token hoặc dữ liệu khách thật.
- Có handoff ghi thay đổi, test, rủi ro còn lại và kết quả cổng. Nếu có sửa mã sau test, chạy lại các test bị ảnh hưởng trước khi dùng PASS cũ.

**Nếu FAIL: sửa và chạy lại trong cùng bước; chưa bắt đầu bước sau.** Test của bước trước bị ảnh hưởng bởi sửa mới phải chạy lại. Không cần chạy toàn bộ dự án sau mỗi chỉnh sửa nhỏ; D11 có lượt hồi quy toàn diện của bản Web. M00–M03 là phạm vi để sau, không ghi như test Web bị bỏ qua và không đổi trạng thái thành PASS.

### 4.2 Cổng review và tích hợp

Roadmap tuân theo [quy trình phát triển GaoApp](GAOAPP-DEVELOPMENT-WORKFLOW.md). `TEST PASS` là kết quả kiểm tra của bước, **không phải `TASK PASS`**, quyền commit, quyền merge hay cho phép triển khai.

Task chạm schema, tiền, tồn kho, tenant, transaction hoặc chống trùng là Level C: có review độc lập và required CI theo contract. Trước khi tích hợp kết quả làm nền cho task tiếp theo, Coordinator phải xác nhận cổng theo workflow: review và CI cùng đạt trên đúng immutable commit SHA. Không tự gọi handoff của người viết mã là review độc lập. Roadmap này chưa cấp quyền commit/push/merge/deploy và không tự khởi tạo các agent thực hiện.

### 4.3 Bằng chứng cần lưu

Mỗi bước khi bắt đầu sẽ có contract và thư mục evidence riêng, ví dụ `Logs/delivery-roadmap/D08/<run-id>/`. Bảng tiến độ trong tài liệu này dẫn đến báo cáo đó. Contract cần ít nhất: nguồn thực, phạm vi/file được sửa, dependency, test case, lệnh chạy, dữ liệu test, migration nếu có, tiêu chí chuyển bước và rollback.

Mẫu báo cáo:

```text
Bước / revision:
Branch / HEAD / hash nguồn thực:
Contract / file thay đổi:
Lệnh build và test thực tế:
SQL / Web / máy in đã dùng; Android chỉ với bước M:
Số test phát hiện / pass / fail / skip:
Kết quả nghiệp vụ và tổng đối soát:
Evidence:
Lỗi còn lại:
Test gate: CHƯA CHẠY | FAIL | CHƯA ĐỦ ĐIỀU KIỆN | TEST PASS
Review / CI / integration gate:
Được chuyển bước: Có | Chưa
```

## 5 Đợt một Web tại tiệm và test bắt buộc

### D00 Khóa nền dự án và môi trường Web

**Sản phẩm:** baseline nguồn, test nền, môi trường Web/SQL an toàn và danh sách máy/quầy/máy in tại tiệm. Chọn số nhân viên/màn hình dùng để thử tải và mục tiêu độ trễ cập nhật nghiệp vụ.

**Việc làm:** lập contract; giữ nguyên working tree hiện hữu; ghi bản .NET/SQL/trình duyệt; đọc luồng POS/kho/ca/giám sát; kiểm tra đăng nhập tại các máy sẽ dùng. Khảo sát đầu đọc hỗ trợ QR nếu có và bảo đảm luôn có cách nhập mã đơn bằng bàn phím. Không cần cài công cụ Android hoặc tạo APK trong bước này.

**Test bắt buộc:**

- D00 T01: solution build và bộ hồi quy POS offline, quản lý ca, công nợ, tồn kho, báo cáo và giám sát hiện có chạy trên SQL test; ghi cụ thể lớp nào và số test. Không coi lỗi nền là lỗi đã được bỏ qua.
- D00 T02: fixture tạo/huỷ database test riêng; xác nhận không dùng connection của cửa hàng đang vận hành.
- D00 T03: mở Web test, đăng nhập và thao tác từ máy tại tiệm; xác nhận đường truy cập API, khả năng in và nhập mã bằng bàn phím. Không yêu cầu cài app hoặc thiết bị Android.
- D00 T04: xác nhận source fingerprint và danh sách thay đổi cũ/mới; không mất hoặc sửa nhầm công việc hiện hữu.

**Cổng:** chỉ sang D01 khi nền Web/SQL và các máy tại tiệm cần dùng đã được kiểm chứng. SDK, APK, GPS và quyền vị trí không nằm trong cổng này.

### D01 Khóa nghiệp vụ và kiến trúc

Đã thực hiện phần model/contract: [DELIVERY D01 20261006](DELIVERY-D01-20261006.md), local 259/259 tests T01–T05, 0 fail/skip. Policy thuần chưa nối runtime. Hồ sơ giao độc lập, quầy/ca tạo giữ nguyên, chốt sale ở quầy/ca hiện tại với nguồn kho/cost dispatch; phiên bản đầu một phương thức, chưa advance/mixed payment, không bỏ combo/voucher/thuế/phí chưa hỗ trợ im lặng. Các giới hạn và yêu cầu tích hợp ghi rõ trong báo cáo; không tự mở D02.

**Sản phẩm:** bảng trạng thái/chuyển trạng thái, quyền theo vai trò, hợp đồng API, quan hệ đơn giao với đơn bán và quyết định kho/tiền/giá vốn. Quyết định dài hạn ghi vào `DECISION-LOG.md` khi contract cho phép.

**Việc làm:** khóa cách tách đơn khỏi giỏ POS và ca gốc; xác định nguồn kho, đơn vị, giảm giá/thuế khi giao thiếu, tiền thu tại khách, quy tắc khách được ghi nợ, hủy sau xuất kho, chuyển người giao, hàng hư/mất và người có quyền duyệt. Khóa ngày nghiệp vụ dùng cho doanh thu, kho và ca khi tạo/giao/chốt khác ngày. Chốt tiền mặt/chuyển khoản/công nợ kết hợp nếu hỗ trợ; chưa chốt chính sách này thì không tự thêm nghiệp vụ tài chính. Chốt xử lý đặt cọc/khách đã trả trước: hỗ trợ với test hoặc từ chối rõ trong đợt đầu, không âm thầm coi như tiền mặt.

Khóa người được phép ghi nhận xuất phát, nhập kết quả hộ theo báo cáo, nhận hàng về và xác nhận tiền. Hợp đồng nghiệp vụ/API không phụ thuộc app hoặc GPS; sau này ứng dụng gọi lại các thao tác đã có.

**Test bắt buộc:**

- D01 T01: test ma trận chuyển trạng thái hợp lệ và bị cấm, gồm không được nhảy từ tạo đơn sang đã chốt hoặc từ chưa soạn sang đang giao.
- D01 T02: test quy tắc số lượng/đơn vị, giảm giá/làm tròn và số tiền chỉ tính thực giao; tất cả kịch bản chuẩn ở mục 7 có kết quả cố định.
- D01 T03: test ma trận quyền và phạm vi; người soạn/người giao không có quyền tự xác nhận chuyển khoản hoặc chốt tài chính.
- D01 T04: kiểm tra thiết kế cho tạo tại A → đóng ca A → chốt tại B ngày sau, kho B khác kho A; không xóa lịch sử gốc hoặc nới guard POS thường.
- D01 T05: test nhập hộ ghi đúng người nhập/người giao/nguồn thông tin và giờ ghi nhận; người thiếu quyền không nhập hộ. Thời gian đã đi hoặc báo giao xong không tự chốt tài chính.

**Cổng:** mô hình và các chính sách ảnh hưởng tiền/kho không còn mơ hồ; các test nghiệp vụ executable đạt. Không chỉ nghiệm thu sơ đồ hoặc nội dung mô tả.

### D02 Lưu dữ liệu và API nền an toàn

**Sản phẩm:** model/migration, định danh QR, lịch sử phiên bản, journal kho/tiền, khóa chống trùng, API xác thực và cơ chế sự kiện lưu bền. Chỉ tạo phần schema đã được contract cho phép.

**Test bắt buộc:**

- D02 T01: migrate database test mới và nâng cấp bản schema trước tính năng có dữ liệu POS cũ; FK, index, precision và dữ liệu lịch sử đúng. Không tạo dữ liệu demo/seed ẩn.
- D02 T02: login thật qua HTTP; user thiếu quyền, store khác, kho/legal entity sai bị chặn; QR/token biết được không vượt qua kiểm tra quyền.
- D02 T03: gửi trùng cùng request trả cùng kết quả; cùng khóa nhưng payload khác bị từ chối; sửa cùng phiên bản chỉ một người thắng.
- D02 T04: lỗi giữa transaction rollback dữ liệu và event; sau commit nhưng mất kết nối client, retry không tạo bản ghi thứ hai.
- D02 T05: restart vẫn đọc được đơn và phát lại sự kiện còn chờ; mọi event gắn đúng store và phiên bản. Consumer xử lý event trùng không nhân đôi kết quả.

**Cổng:** các kiểm tra SQL thật, HTTP, concurrency và migration đều đạt; có phương án phục hồi schema/data được review. API nền chưa có nghĩa là luồng giao hàng đã dùng được.

### D03 POS tạo đơn và in bill giao có QR

**Sản phẩm:** lựa chọn đơn giao tại POS, thông tin người nhận/địa chỉ, lưu đơn, bill có QR và mở đúng hồ sơ. Tạo xong POS có thể phục vụ khách tiếp theo.

Chốt ngày 06/10/2026: phiếu giao dùng **A5 portrait**, in bằng trình duyệt, riêng với bill POS nhiệt. Máy code chưa có máy in; code/browser/PDF/QR tự động được kiểm tra trước, in giấy/đầu đọc tại tiệm giữ pending khi có thiết bị. Đây là quyết định của user, không tự chuyển sang khổ 80/58 hoặc mặc định đã kiểm chứng giấy.

**Test bắt buộc:**

- D03 T01: tạo một đơn chỉ có một mã/QR dù nhấn hai lần hoặc mạng mất ngay sau commit. Định danh giải mã từ QR và mã nhập tay mở cùng đơn đã lưu; in lại dùng cùng định danh.
- D03 T02: tại bước tạo không có bán hàng hoàn thành, thu tiền, nợ khách, điểm thưởng hoặc hóa đơn bán phát sinh sớm; chưa xuất kho vật lý.
- D03 T03: tách đơn giao khỏi giỏ, tạo đơn bán khác và đóng ca A được; đơn giao vẫn tồn tại và truy cập được ở ca/quầy có quyền.
- D03 T04: đường tiền mặt/chuyển khoản POS hiện có, QR thanh toán chờ, hold/resume/offline không bị đổi hành vi.
- D03 T05: test browser bill/QR, giải mã đúng nội dung QR và in trên mẫu giấy/máy in thực tế ở tiệm; mã đơn/dòng hàng/địa chỉ rõ, nhập mã tra được đơn. Nếu tiệm dùng đầu đọc QR, kiểm tra đúng đầu đọc đó. Đơn chưa lưu thành công không có bill khẳng định đã nhận đơn giao.

**Cổng:** POS, bill QR/mã đơn và luồng tra cứu bằng máy tại tiệm đạt; đơn mới là hồ sơ giao đang chờ xử lý, không phải doanh thu. Chưa có đầu đọc QR vẫn có thể vận hành bằng nhập mã.

### D04 Nhận soạn bằng QR hoặc mã và xử lý thiếu hàng

**Sản phẩm:** màn hình Web soạn mở bằng QR hoặc nhập mã; một người nhận trách nhiệm; tiến độ từng dòng; đánh dấu thiếu; yêu cầu quản lý xử lý; sửa/bỏ/thay hàng có phiên bản và lịch sử.

**Test bắt buộc:**

- D04 T01: hai nhân viên cùng nhận một đơn chỉ một người được nhận; người khác thấy người đang soạn. Chuyển người soạn cần quyền và audit.
- D04 T02: ghi số lượng đúng đơn vị; không âm, không vượt số lượng được duyệt; thiếu một phần và thiếu toàn bộ có nội dung cụ thể.
- D04 T03: giảm/bỏ/thay hàng cập nhật giá và lượng theo chính sách D01, liên kết dòng gốc, lý do và người duyệt; bill cũ cảnh báo phiên bản đã đổi.
- D04 T04: thiếu hàng được quản lý nhìn thấy từ dữ liệu lưu bền; thao tác lỗi không báo đã xử lý. Không cho hoàn tất soạn khi lượng còn mâu thuẫn.
- D04 T05: khi đơn đã bàn giao thì chỉnh giỏ kiểu tự do bị chặn; muốn đổi phải đi qua luồng điều chỉnh/thu hồi hợp lệ.
- D04 T06: toàn bộ nhận/soạn/báo thiếu/hoàn tất chạy bằng bàn phím và trình duyệt trên máy tại tiệm; QR và nhập mã dùng cùng kiểm tra quyền/phiên bản. Mã không tồn tại hoặc thuộc store khác không lộ nội dung đơn.

**Cổng:** browser/API/SQL và tranh nhận việc đạt. Đến đây chỉ kiểm chứng soạn; chưa bàn giao hàng thật trước D05.

Local D04 được nghiệm thu theo READY r22/r23: 798/798 affected SQL/HTTP/schema, 31 semantic/metadata guards trong M01, Node239 giữ lại với đúng49 inputs không đổi; browser D03 8 nhóm và D04 10 nhóm, đủ8 trang A5 đã xem; deployment11 và published Production2. Hai tình huống đủ và thiếu/thay/duyệt/replay/reassign chạy đạt trên clone dữ liệu thật. Bằng chứng bảo toàn151 bảng/7.731.232 rows khi upgrade68→71 được nghiệm thu riêng từ lượt đã giữ, không gọi là whole fingerprint chạy lại trong lượt business thành công. Build Release/Browser/paired publisher không có compiler warning/error; EF runtime warnings và giới hạn stock-child environment giữ nguyên trong [báo cáo D04](DELIVERY-D04-20261006.md). Máy in/đầu đọc thật vẫn chờ thiết bị; luồng giao giỏ có voucher/product/combo promotion vẫn bị D03 từ chối rõ. Những kết quả local này chưa mở D05 hoặc thay review/CI.

### D05 Giữ hàng và xuất trả kho đúng một lần

**Sản phẩm:** nối soạn với giữ hàng, bàn giao với xuất kho, nhận lại với trả kho. Tách hàng đang do người giao giữ khỏi hàng sẵn sàng bán; giữ nguồn giá vốn và legal entity.

**Test bắt buộc:**

- D05 T01: nhiều đơn giao và POS cùng tranh lượng cuối không bán vượt hàng có thể dùng; thiếu hàng không tạo giữ/xuất giả.
- D05 T02: sửa/hủy trước bàn giao giải phóng đúng lượng giữ; gửi trùng lệnh bàn giao chỉ một lần xuất kho.
- D05 T03: xuất 8, giao 6, nhận về 2 có journal rõ và lượng kho cuối đúng; không nhập lại hàng chỉ vì nhân viên báo mang về mà kho chưa nhận.
- D05 T04: chốt bán sau xuất không xuất lần nữa; giá vốn cuối chỉ thuộc hàng thực giao, phần trả về không bị tính vào hàng bán.
- D05 T05: dòng quy đổi, nhiều nguồn cost layer và multi legal entity giữ nguyên nguồn; sai store/kho/legal owner bị chặn; không lấy kho quầy chốt thay nguồn xuất.
- D05 T06: lỗi khi xuất/trả/ghi nguồn giá vốn rollback đồng bộ; retry/restart không trùng inventory movement. Hàng hư/mất đi theo xử lý riêng có quyền, không tự nhập lại hàng bán được.

**Cổng:** các invariant tồn kho/giá vốn đạt trên SQL thật. Không chấp nhận cân bằng tiền nhưng tồn kho sai.

### D06 Phân công bàn giao và ghi nhận xuất phát

**Sản phẩm:** màn hình Web chọn người giao và đơn mang theo, xác nhận bàn giao, nút ghi nhận xuất phát và giờ đi. Một chuyến có thể mang nhiều đơn; mỗi đơn có trách nhiệm đang giữ hàng rõ ràng. Ghi giờ xuất phát không cần người giao đăng nhập điện thoại.

**Test bắt buộc:**

- D06 T01: chỉ đơn đủ điều kiện mới bàn giao; mỗi đơn đang xuất phát chỉ có một người/chuyến nhận trách nhiệm.
- D06 T02: hai người cùng nhận hoặc đổi người giao khi đang nhận không làm mất/trùng hàng. Chuyển trách nhiệm có người duyệt và nhật ký.
- D06 T03: gộp nhiều đơn không trộn dòng/khách/kho; hoàn tất một điểm không tự hoàn tất cả chuyến.
- D06 T04: hủy sau xuất kho bắt buộc đối soát hàng; không chỉ đổi trạng thái rồi làm mất journal. Người không có quyền cập nhật/người giao không được phân công/cửa hàng khác không cập nhật được; quyền nhập hộ có phạm vi riêng.
- D06 T05: người bàn giao ghi nhận xuất phát cho người giao đúng một lần, lưu cả hai người và giờ server; sửa giờ báo hoặc chuyển người giao có audit. Đồng hồ thời gian đã đi không tự tạo trạng thái đã giao/đã về.

**Cổng:** chạy API/browser và test concurrency đạt, danh sách chuyến phản ánh hàng đã bàn giao thật.

### D07 Nhập kết quả giao và ghi nhận trở về

**Sản phẩm:** màn hình Web tại tiệm ghi kết quả từng đơn/dòng, lý do không giao, tiền người giao báo đã thu và lượng mang về. Người có quyền có thể nhập hộ khi người giao gọi về; nếu không có báo cáo giữa chuyến thì cập nhật khi trở về. Trở về và đã đối soát/chốt là các mốc riêng.

**Test bắt buộc:**

- D07 T01: đủ/thiếu/khách từ chối/không liên hệ được/hủy có lượng thực giao và lượng mang về khớp lượng đã nhận; kết quả không thể vượt hàng đang giữ.
- D07 T02: ghi nhận tiền thu tại khách không tự hoàn thành bán hoặc chốt ca; chuyển khoản do người giao báo vẫn cần người có quyền xác nhận đã nhận.
- D07 T03: người nhập hộ, người giao, nguồn báo và thời điểm server lưu được truy vết. Giờ giao do nhân viên khai báo nếu có được lưu riêng; báo qua điện thoại không giả thành xác nhận hệ thống tự theo dõi ngoài đường.
- D07 T04: retry, báo muộn, hai tab hoặc kết quả cùng phiên bản không ghi đè kết quả đã khóa; thông tin cũ sau khi đã chốt không sửa được tài chính. Chỉ ghi nhận trở về không tự nhập hàng hoặc ghi thu tiền khi chưa xác nhận bàn giao.
- D07 T05: đơn giao 0 hàng kết thúc qua luồng không giao được/thu hồi hàng, không tạo một lần bán hoặc nợ khách cho hàng chưa giao.
- D07 T06: chạy đủ chuyến không cập nhật giữa đường; khi người giao về, nhân viên nhập đầy đủ kết quả và chuyển qua đối soát được. Địa chỉ/khách giữ đúng snapshot; mọi thao tác hoàn thành trên máy tại tiệm, không cần GPS/Maps/app.

**Cổng:** dữ liệu kết quả, nhập hộ, trở về và công thức đối soát đạt bằng Web/SQL/HTTP. Trạng thái là kết quả đã được người có quyền ghi nhận, không phải vị trí hoặc hoạt động được tự động xác minh ngoài đường.

### D08 Bàn giao tiền hàng và chốt tại quầy bất kỳ

**Sản phẩm:** màn hình đối soát hàng thực giao, hàng nhận về, tiền khách trả, tiền người giao bàn giao; xử lý lệch; chốt ở quầy/ca hiện hành. Tạo dữ liệu bán, thu tiền/công nợ và các ghi nhận liên quan đúng thời điểm.

**Test bắt buộc:**

- D08 T01: A tạo, ca A đã đóng, B có ca khác chốt ngày sau; ghi đúng quầy/ca B, giữ nguyên lịch sử A và kho A, kể cả B dùng kho khác. Quầy store khác bị chặn.
- D08 T02: tiền mặt vào đúng ca B; đã nhận chuyển khoản vào đúng ghi nhận không tiền mặt và không tạo QR chuyển khoản mới; khách đủ điều kiện ghi nợ đúng phần hàng thực giao. Khách chưa đủ điều kiện không tự được cấp công nợ.
- D08 T03: dòng không giao không bị tính tiền; chiết khấu/thuế/làm tròn và trường hợp kết hợp thanh toán nếu D01 cho phép có tổng đúng.
- D08 T04: thu 120.000 nhưng người giao bàn giao 110.000 hiện lệch 10.000 và yêu cầu người có quyền xử lý; không tự đẩy 10.000 vào nợ khách hoặc báo đã đủ tiền.
- D08 T05: hai quầy chốt đồng thời, double click, timeout sau commit và replay chỉ tạo một kết quả bán/tiền/nợ; lỗi giữa chừng rollback toàn bộ.
- D08 T06: tồn kho không bị trừ lại; giá vốn/doanh thu/báo cáo/điểm thưởng/hóa đơn cuối đúng một lần và đúng phần thực giao. Hoàn hàng sau bán không nhập lại phần đã nhận về ở D05.
- D08 T07: người giao/người soạn không tự chốt; người chốt cần quyền và ca mở. Điều chỉnh sau chốt dùng luồng có audit, không sửa trực tiếp lịch sử.

**Cổng:** bộ đối soát tiền/kho/ca/báo cáo, concurrency và phân quyền đạt trên SQL + HTTP + browser. Đây là cổng tài chính bắt buộc, không rút gọn để kịp lịch.

### D09 Theo dõi hàng khách còn thiếu

**Sản phẩm:** danh sách thiếu theo khách, đơn gốc, mặt hàng, số lượng, lý do và người xử lý; trạng thái cần liên hệ/chờ hàng/khách đồng ý giao bù/khách bỏ qua. Có hàng về thì tạo nhắc việc nội bộ.

**Test bắt buộc:**

- D09 T01: thiếu hàng sau soạn hoặc sau giao chỉ tạo lượng cần theo dõi phù hợp theo quyết định D01; không tạo nợ tiền. Phân biệt khách còn muốn nhận với hàng khách đã từ chối.
- D09 T02: nhập hàng về làm hiện đúng khách/mặt hàng cần liên hệ, không tự nhận hàng đã dành chắc chắn cho khách và không tự tạo hóa đơn.
- D09 T03: khách đồng ý nhận bù tạo đơn/chuyến tiếp theo liên kết nguồn thiếu; chỉ trừ phần thiếu đã thực giao, chỉ tính tiền lần giao mới. Retry không tạo hai đơn bù.
- D09 T04: khách bỏ qua đóng đúng lượng thiếu với lý do; không đổi tiền/kho của lần bán đã chốt. Hai nhân viên xử lý cùng phần thiếu không vượt lượng còn lại.

**Cổng:** danh sách, liên kết lần giao sau và các test chống tính tiền trùng đạt. Không sửa ngược đơn bán đã hoàn thành để thêm hàng mới.

### D10 Kết nối màn hình văn phòng và hoàn thiện hiệu ứng

**Sản phẩm:** màn hình quản lý đang dùng có dữ liệu giao hàng từ Web/SQL và nhận cập nhật sau khi nhân viên lưu. Kế thừa demo đã được đồng ý; thấy người soạn, người giao, đơn cần xử lý, giờ đi, thời gian đã đi, lần cập nhật cuối và đơn chờ đối soát/chốt.

**Thiết kế dự kiến:** thẻ nhân viên/đơn theo giai đoạn, thanh tiến độ theo lượng soạn/giao đã ghi nhận, hiệu ứng ngắn khi chuyển mốc, cảnh báo thiếu hàng/lệch tiền. Ví dụ thẻ chuyến hiện “Anh Nam đang giao 3 đơn — xuất phát 14:20 — đã đi 25 phút — cập nhật cuối 14:20”. Thời gian đã đi là đồng hồ tính từ mốc xuất phát được ghi nhận; không phải vị trí GPS. Không có xe chạy trên tuyến bản đồ hoặc phần trăm giao tự tăng khi chưa có cập nhật. Có toàn màn hình, chữ đọc được từ vị trí ngồi thực tế và giảm chuyển động.

**Test bắt buộc:**

- D10 T01: hai phiên quản lý và nhân viên tạo/soạn/xuất phát/nhập kết quả/chốt; màn hình nhận đúng trạng thái sau commit. Mục tiêu cập nhật nghiệp vụ p95 không quá 3 giây ở mạng thử ổn định, khóa điều kiện đo tại D00.
- D10 T02: F5, restart Web, mất SSE/reconnect và event trùng phục hồi snapshot SQL đúng; không mất đơn đang giao hoặc nhân đôi thẻ/số lượng.
- D10 T03: chuyến không có cập nhật giữa đường vẫn hiện đúng người giao/giờ đi/lần ghi nhận cuối; đồng hồ không tự hoàn tất đơn, không tạo vị trí hoặc tiến độ giả. Kết quả được báo, hàng nhận về và tiền đã đối soát là các thông tin riêng.
- D10 T04: chỉ quản lý có quyền đúng store xem; thu hồi quyền đóng dữ liệu đang phát. Dữ liệu khách trên màn hình đúng mức cần dùng.
- D10 T05: nghiệm thu trên màn hình văn phòng thật, cả nhiều đơn và ít đơn; không chữ tràn/nhảy bố cục, cảnh báo đọc được, chế độ giảm chuyển động đạt.
- D10 T06: khi mất kết nối Web thì báo dữ liệu cũ và không phát hiệu ứng thành công; reconnect đọc lại snapshot. Chưa có báo cáo mới từ người giao khác với mất kết nối của màn hình quản lý.

**Cổng:** trạng thái theo các mốc, restart/security/performance và bố cục thực đạt, độc lập với app/GPS. Một Web instance là phạm vi đầu; cần nhiều instance thì kho pub/sub dùng chung phải có trước khi bật.

### D11 Test trọn luồng Web và hồi quy toàn hệ thống

**Sản phẩm:** báo cáo tổng hợp trên đúng nguồn ứng viên, từ POS/máy in/máy soạn/máy quầy đến SQL và màn hình văn phòng. Chứng minh luồng hoàn chỉnh vận hành không cần điện thoại, GPS hoặc key bản đồ.

**Test bắt buộc:**

- D11 T01: chạy toàn bộ kịch bản mục 7 bằng HTTP/browser và máy tại tiệm, đối chiếu database/báo cáo; có cả chuyến không báo giữa đường và chuyến nhân viên nhập hộ. Không yêu cầu APK hoặc GPS.
- D11 T02: hồi quy POS tiền mặt, chuyển khoản/QR, công nợ, đặt cọc, giữ/mở lại/offline, đóng ca, hủy/hoàn hàng, cost, hóa đơn và báo cáo hiện hữu.
- D11 T03: thử gửi trùng và đồng thời ở tạo/nhận soạn/bàn giao/kết quả/chốt/giao bù; mất kết nối sau commit và restart giữa luồng. Không chốt tiền hoặc xuất hàng trùng.
- D11 T04: test hai store, nhiều vai trò, quyền bị thu hồi, phiên hết hạn, ID bị đoán và request sửa người/chuyến/kho/nguồn báo. Không đọc/ghi vượt boundary hoặc giả danh người giao khi nhập hộ.
- D11 T05: thử tải theo quy mô D00, gồm cập nhật các mốc, stream và POS cùng lúc ít nhất 45 phút; ghi p95/p99, lỗi, độ trễ và tài nguyên. Nếu chưa có quy mô thật, khởi điểm đề xuất là 20 người thao tác và 5 màn hình giám sát, khóa trước khi đo; không có bài test GPS trong đợt này.
- D11 T06: đối chiếu lượng đã xuất = thực giao + đã nhận lại + còn đang giữ + phần mất/hư đã xử lý theo policy; giá trị bán được giải thích bằng tiền thực nhận/chuyển khoản đã xác nhận/nợ khách hợp lệ và chênh lệch người giao được duyệt nếu có. Giá vốn/legal owner khớp nguồn; không ghi tiền mặt kỳ vọng vượt tiền thực nhận chỉ để cân tổng.
- D11 T07: mất mạng, reload, logout/đổi nhân viên, tab cũ sau nâng cấp Web không gửi thao tác chưa lưu dưới danh tính mới hoặc hiển thị như đã chốt. Trạng thái chưa xác nhận phải tra lại server; gửi lại dùng khóa chống trùng.

**Cổng:** toàn bộ suite Web bắt buộc đạt, không còn lỗi chặn; review/required CI của ứng viên đạt cùng SHA khi tích hợp. Test ở nguồn cũ không thay kết quả cho nguồn mới. Các bước M để sau không ảnh hưởng verdict của bản Web.

### D12 Nghiệm thu tại tiệm và phát hành bản Web

**Sản phẩm:** bản Web/API release đã nghiệm thu; hướng dẫn cho thu ngân, soạn, bàn giao và quản lý; kế hoạch bật theo nhóm, khôi phục và hỗ trợ. Có thể dùng bản này trước khi triển khai bất kỳ bước M nào. Chỉ triển khai khi có yêu cầu và quyết định vận hành phù hợp.

**Test bắt buộc:**

- D12 T01: người tại tiệm dùng máy in, máy soạn/quầy và màn hình thật chạy đơn đủ, đơn thiếu, đơn không giao được, đơn chốt ở quầy khác và đơn nhập hộ. Chạy được khi không dùng điện thoại; có xác nhận nghiệp vụ và đối soát.
- D12 T02: backup có kiểm chứng phục hồi; nâng cấp schema trên môi trường được phép; package manifest/hash đúng nguồn review. Schema chạy theo TH2 với `--schema-only`, không tự seed menu/quyền/dữ liệu.
- D12 T03: cấp menu/quyền theo delta đã kiểm tra, gồm quyền nhập hộ, bảo toàn tùy chỉnh. Không dùng full security seed làm cách thử sửa menu thiếu.
- D12 T04: rollout có tắt/bật tạo đơn giao; khi ngừng nhận đơn mới vẫn đọc và xử lý an toàn đơn đang chạy. Không có dependency khởi động vào Android/GPS/Maps; mất các thành phần này không chặn bản Web.
- D12 T05: diễn tập khôi phục theo runbook. Rollback Web không tự xóa lịch sử giao/tiền hoặc chạy Down migration. Nếu có dữ liệu sau backup, phương án giữ dữ liệu/forward-fix phải rõ trước release.

**Cổng:** UAT và kiểm tra release/restore đạt, có quyết định vận hành theo [runbook hiện hành](GAOAPP-DEPLOYMENT-RUNBOOK-20260913.md). Đây là cổng hoàn thành **đợt một**. Test development không cấp quyền tự triển khai production; không dùng runbook staging cũ thay thế.

## 6 Đợt hai app và GPS để sau

Các bước dưới đây có trạng thái **ĐỂ SAU**, không thực hiện trong đợt Web và không có test được coi là PASS. Khi bắt đầu đợt hai, đọc lại nguồn sau D12, khóa contract và kiểm tra lại tài liệu nền tảng Android/Maps theo phiên bản thực tế. Luồng Web vẫn hoạt động nếu chưa bật hoặc phải tắt app/GPS.

### M00 Kiểm chứng Android và khóa phạm vi bổ sung

**Sản phẩm:** toolchain Android, kết nối thiết bị thật, APK kiểm chứng và hợp đồng API bổ sung. Chọn provider bản đồ, quyền vị trí, lưu lịch sử và mục tiêu độ trễ/pin. Hỗ trợ iPhone vẫn là khả năng tương lai, không tự thêm vào đợt này.

**Test bắt buộc:**

- M00 T01: APK tối thiểu build tái lập, cài/mở và quét QR trên máy Android thật; ghi SDK/toolchain/OS và hash APK.
- M00 T02: điện thoại truy cập API test qua HTTPS được tin cậy, đăng nhập đúng store và mở đúng đơn; không vô hiệu hóa kiểm tra chứng chỉ.
- M00 T03: contract/API bổ sung dùng lại dữ liệu đã có; luồng Web vẫn chạy khi chưa bật tính năng mobile/GPS. Khóa chính sách phiên bản client và giới hạn lưu vị trí.

**Cổng:** toolchain và kết nối máy thật được chứng minh, không ảnh hưởng bản Web đang dùng. Chỉ xét cổng này khi bắt đầu đợt hai.

### M01 Ứng dụng Android cập nhật công việc

**Sản phẩm:** app đăng nhập, quét QR, xem việc, nhận chuyến, cập nhật thực giao/tiền thu và bàn giao; soạn nếu có quyền. Địa chỉ có thể chọn điểm bản đồ và mở Google Maps. Mở Maps là tiện ích chỉ đường, tách khỏi upload GPS. [Maps URLs](https://developers.google.com/maps/documentation/urls/get-started).

**Test bắt buộc:**

- M01 T01: camera/quét QR, đăng nhập/hết hạn/thu hồi phiên và thao tác chạy trên Android thật; đúng phân quyền. Người giao chỉ thao tác việc được giao, không tự chốt tài chính.
- M01 T02: mất mạng lưu thao tác phù hợp để gửi lại với cùng idempotency key; nhận việc/sửa lượng khi chưa có server xác nhận không hiện như đã thành công.
- M01 T03: đóng app/khởi động lại/đổi mạng khi gửi không mất hoặc trùng kết quả; app và Web/nhập hộ cùng sửa thì kiểm soát phiên bản, không im lặng ghi đè.
- M01 T04: logout/đổi nhân viên không gửi hàng đợi cũ dưới danh tính mới; token không lộ trong log/QR. Điểm đến và URL chỉ đường đúng khách/chuyến.
- M01 T05: unit/instrumented test đạt, APK debug/release build tái lập; bản release chạy thao tác thật, khóa ký không nằm trong repo. [Hướng dẫn test Android](https://developer.android.com/studio/test).

**Cổng:** app dùng được với server thật trong môi trường test, dữ liệu/tiền/kho và quyền vẫn đúng; chưa khẳng định có GPS nếu M02 chưa đạt.

### M02 GPS trong chuyến giao

**Sản phẩm:** chia sẻ vị trí khi có chuyến hoạt động, lưu vị trí cuối và lịch sử giới hạn; có chỉ báo trên điện thoại, tuổi tín hiệu ở server. Thiết kế theo quyền và foreground service của Android/target SDK đã chọn. [Quyền vị trí](https://developer.android.com/develop/sensors-and-location/location/permissions), [loại foreground service](https://developer.android.com/develop/background-work/services/fgs/service-types).

**Mục tiêu để khóa ở M00:** đề xuất gửi khoảng 10 giây khi di chuyển, vị trí hiển thị mới trong 30 giây ở mạng thử ổn định, quá 60 giây báo cũ và dừng hiệu ứng chạy. Đo pin/độ trễ trên thiết bị thật; không thay tiêu chí sau khi FAIL để ghi PASS.

**Test bắt buộc:**

- M02 T01: di chuyển thật, mở Maps và tắt màn hình ít nhất 30 phút; bản release tiếp tục gửi theo điều kiện Android, đo độ trễ/mất mẫu/pin. Emulator không thay test thiết bị thật.
- M02 T02: từ chối quyền, vị trí gần đúng, tắt GPS, tiết kiệm pin hoặc hệ điều hành dừng app hiện đúng tình trạng; không gọi gần đúng là chính xác, không cam kết upload sau force-stop.
- M02 T03: mất mạng báo tín hiệu cũ; mẫu gửi bù có giờ lấy/giờ nhận riêng, không giả thành vị trí mới hoặc làm xe chạy theo điểm cũ.
- M02 T04: nhân viên chỉ gửi cho chuyến được phân công trong đúng store; thu hồi phiên/quyền ngừng upload hoặc xem theo policy.
- M02 T05: kết thúc chuyến và bàn giao dừng theo dõi; nếu chuyến còn quay về tiệm thì không tự kết thúc ở khách cuối. Logout không tiếp tục upload.
- M02 T06: restart giữ vị trí cuối với tuổi đúng; giới hạn lưu/xóa lịch sử được test; tọa độ sai bị từ chối.

**Cổng:** test thiết bị thật và quyền/nền đạt, không ảnh hưởng xử lý đơn khi không có GPS. GPS không tự xác nhận giao hàng/thu tiền.

### M03 Kết nối vị trí nghiệm thu và phát hành bổ sung

**Sản phẩm:** bản đồ/vị trí thật trên màn hình D10, giữ nguyên cập nhật theo các mốc của Web; APK và backend bổ sung đã nghiệm thu.

**Test bắt buộc:**

- M03 T01: map hiển thị đúng nhân viên/chuyến/tuổi vị trí; mất GPS dừng animation và trở về thông tin theo mốc. Provider/key nếu cần được kiểm chứng, không giả định Maps URL cung cấp map nhúng.
- M03 T02: hồi quy các test Web bị ảnh hưởng, tiền/kho/nhập hộ và xung đột app–Web. Nâng cấp/thu hồi app không làm hỏng đơn đang chạy; phiên bản cũ bị cảnh báo/chặn an toàn theo policy.
- M03 T03: thử tải GPS + stream + POS và nghiệm thu giao trên Android thật; đối chiếu tiền/kho, dùng Maps/tắt màn hình và mất mạng/reconnect.
- M03 T04: APK/package/hash đúng nguồn review; rollout và khôi phục backend/mobile được kiểm tra theo runbook. Tắt GPS/mobile vẫn xử lý được chuyến qua Web, không xóa lịch sử/tiền.

**Cổng:** review/CI, UAT thiết bị thật và release evidence của phần bổ sung đạt; chỉ phát hành khi có quyết định vận hành. Đợt hai hoàn tất tại đây, không dùng để đánh giá lại rằng đợt Web chưa hoàn thành chỉ vì chưa có GPS.

## 7 Bộ kịch bản chuẩn có kết quả đối chiếu

### 7.1 Dữ liệu gốc

Dùng database test riêng, mỗi kịch bản khởi tạo độc lập. Cùng cửa hàng S1 có quầy A xuất từ W1 và quầy B dùng W2; cửa hàng S2 là đối tượng kiểm tra chặn. Giá trong kịch bản này không có giảm giá/thuế/phí để số liệu đối chiếu rõ. Các tình huống giá/thuế/đơn vị khác có bộ test riêng ở D01/D08.

| Hàng | Tồn W1 ban đầu | Giá bán mỗi đơn vị | Giá vốn đã xác định |
| --- | ---: | ---: | ---: |
| A | 100 | 20.000 | 10.000 |
| B | 0 | 30.000 | 15.000 nếu có nguồn ở lần nhập sau |

Quầy A tạo yêu cầu A × 8 và B × 2, tổng dự kiến **220.000**. Lúc tạo: doanh thu, thu tiền, nợ khách và hóa đơn bán đều chưa phát sinh; tồn vật lý A vẫn 100. Soạn được A × 8, thiếu B × 2. Quản lý duyệt giao phần có hàng; phiếu hiện tại và phần thiếu được lưu rõ.

### 7.2 Xuất giao và nhận về

- Bàn giao A × 8: tồn vật lý W1 của A còn 92; lượng 8 đang người giao giữ có nguồn xuất/giá vốn truy vết.
- Khách nhận A × 6, người giao mang về A × 2. Chỉ sau khi kho xác nhận nhận lại 2, tồn W1 của A là **94**. B không có movement trong lần giao này.
- Giá trị hàng thực bán là **120.000**; giá vốn bán là **60.000** đúng một lần. B thiếu không tính tiền; A mang về không tính tiền. Quyết định khách muốn nhận tiếp A còn lại hay bỏ phải ghi riêng, không suy từ việc trả kho.

### 7.3 Chốt tại quầy B

Mỗi dòng dưới đây là một lần chạy độc lập trên cùng dữ liệu gốc, không phải cộng dồn thanh toán.

| Tình huống | Kết quả bắt buộc |
| --- | --- |
| Tiền mặt đủ | Ca B nhận 120.000; ca A không nhận tiền từ đơn này. Chốt khi ca A đã đóng vẫn đạt; nguồn kho/cost là W1. |
| Đã nhận chuyển khoản | Ghi đã nhận 120.000 theo phương thức không tiền mặt tại lần chốt B; không tạo QR chuyển khoản mới; không tăng tiền mặt kỳ vọng. |
| Công nợ đủ điều kiện | Nợ khách 120.000; tiền mặt kỳ vọng không tăng; thiếu B × 2 không thêm 60.000 vào nợ. |
| Kết hợp nếu D01 cho phép | Tiền mặt 70.000 và nợ hợp lệ 50.000; tổng 120.000, không gán nợ cho lượng chưa giao. |
| Người giao thu đủ nhưng bàn giao thiếu | Khách đã trả 120.000, người giao chỉ giao 110.000: hiện chênh lệch 10.000 cần xử lý; không ghi khách nợ thêm 10.000. Không ghi ca đã nhận tiền mặt 120.000 khi thực nhận 110.000. |
| Hai quầy cùng chốt hoặc client retry | Chỉ một lần bán/chốt/ghi tiền/nợ/giá vốn/điểm/hóa đơn. Tồn cuối A vẫn 94. |
| Quầy thuộc S2 | Bị từ chối, không có thay đổi dữ liệu hoặc event thành công. |

Sau khi chốt, hoàn lại một phần A đã bán phải dùng luồng hoàn hiện hữu phù hợp với nguồn xuất; không hoàn thêm A × 2 đã mang về trước bán. Một đơn giao 0 hàng không tạo doanh thu/nợ/hóa đơn cho lượng đã đặt.

### 7.4 Theo dõi B còn thiếu

B × 2 còn ở danh sách cần liên hệ, **nợ tiền bằng 0** cho phần này. Khi W1 nhập được B, quản lý nhận tác vụ liên hệ. Khách đồng ý nhận thì tạo đơn bù liên kết đơn gốc; nếu thực giao B × 2 ở mức giá 30.000 đã được xác nhận, lần bán tiếp là **60.000**, tính một lần. Khách bỏ qua thì đóng phần thiếu mà không sửa 120.000 đã chốt ở lần đầu.

### 7.5 Lỗi và phục hồi phải thử

Các biến thể của đợt Web áp dụng ở bước tương ứng: hết hàng đúng lúc giữ; mất mạng sau commit; hai người nhận cùng đơn; chốt đồng thời; quyền bị thu hồi; ca tạo đã đóng; server restart; tab đóng/reload; nhập hộ sai người; báo cáo muộn; bill QR phiên bản cũ; hủy trước và sau xuất; hàng bị hư/mất; báo cáo chạy qua ngày. Kết quả phải đối chiếu trạng thái và sổ kho/tiền, không chỉ thông báo trên màn hình. App bị đóng/vị trí cũ gửi bù thuộc M01/M02, không là điều kiện đợt Web.

### 7.6 Hai cách cập nhật không cần app

**Chỉ cập nhật khi trở về:** người tại tiệm ghi nhận xuất phát lúc 14:20. Đến 14:45, màn hình vẫn hiện đang giao, đã đi 25 phút, cập nhật cuối 14:20; không tự biết vị trí hoặc báo giao xong. Khi người giao về, nhân viên nhập kết quả giao A × 6, mang về A × 2 và tiền thu; người nhận bàn giao xác nhận hàng/tiền, quầy B chốt theo mục 7.3. Toàn bộ thao tác bằng Web trên máy tại tiệm.

**Có báo cáo giữa chuyến:** người giao gọi về, nhân viên có quyền ghi nhận hộ kết quả A × 6 và tiền được báo đã thu. Lưu người nhập, người giao, nguồn báo và giờ server nhận; giờ khách nhận hàng nếu được khai báo là trường riêng. Màn hình có hiệu ứng cho lần cập nhật mới và nhãn nguồn báo; chưa tự nhập A × 2 lại kho hoặc ghi tiền vào ca khi tiệm chưa nhận. Khi người giao về vẫn phải bàn giao/đối soát như cách đầu. Báo muộn sau khi kết quả đã khóa không sửa tài chính đã chốt.

## 8 Lệnh kiểm tra và cách tổ chức test

Các lệnh dưới đây là mẫu để khóa trong contract từng bước; **chưa chạy trong lượt lập roadmap**. Không chạy lên SQL cấu hình vận hành. Fixture SQL test phải tạo database riêng có tên và cleanup được kiểm soát; [runsettings hiện có](../../GaoApp.Tests/test.local.runsettings) chọn `.\SQLEXPRESS` theo nội dung file, cần kiểm tra instance thực tế tại D00.

Build nền:

```powershell
dotnet build .\GaoApp.sln --configuration Release --nologo
dotnet build .\GaoApp.Tests.Browser\PosOffline.Browser.csproj --configuration Release --nologo
```

Bộ test giám sát đã tồn tại dùng lại khi kiểm tra hồi quy:

```powershell
dotnet test .\GaoApp.Tests\GaoApp.Tests.csproj --configuration Release --settings .\GaoApp.Tests\test.local.runsettings --filter "FullyQualifiedName~StoreActivityTests|FullyQualifiedName~StoreMonitorHttpTests|FullyQualifiedName~EndpointSecurityCoverageTests" --logger "trx;LogFileName=store-monitor-regression.trx" --nologo
```

Test tự động mới của đợt Web nên có category `DeliveryD01` … `DeliveryD11` và test ID trong mục 5; D00 dùng test nền hiện hữu, D12 có release/script/UAT theo contract. Đợt hai dùng category riêng cho các bước M khi triển khai. Đây là **quy ước dự kiến**, chưa có suite giao hàng này. Khi viết từng bước phải khóa tên lớp/case và số lượng tối thiểu; lệnh thành công với category chưa tồn tại không được đánh dấu PASS.

```powershell
# Ví dụ cho D08 sau khi suite đã được viết và kiểm tra discovery.
dotnet test .\GaoApp.Tests\GaoApp.Tests.csproj --configuration Release --settings .\GaoApp.Tests\test.local.runsettings --list-tests --filter "Category=DeliveryD08" --nologo
dotnet test .\GaoApp.Tests\GaoApp.Tests.csproj --configuration Release --settings .\GaoApp.Tests\test.local.runsettings --filter "Category=DeliveryD08" --logger "trx;LogFileName=delivery-d08.trx" --results-directory .\Logs\delivery-roadmap\D08\<run-id> --nologo
```

Thay `<run-id>` bằng mã lượt chạy thực trước khi dùng lệnh. Category không thay được SQL/HTTP/browser/máy in/UAT bắt buộc trong contract; device test chỉ bắt buộc cho bước M. API/SQL tests tiền, kho, concurrency dùng SQL Server thật; EF InMemory chỉ phù hợp cho test thuần không cần chứng minh transaction/lock/index.

Browser runner hiện nằm ngoài solution nên phải build/chạy riêng. Probe giám sát có sẵn:

```powershell
$env:GAOAPP_TEST_SQL_SERVER='.\SQLEXPRESS'
dotnet run --project .\GaoApp.Tests.Browser\PosOffline.Browser.csproj --configuration Release --no-build --no-restore -- --store-monitor
```

Probe `--delivery` đã bổ sung ở D03 với fixture SQL tự tạo/xóa và A5/lookup/mất phản hồi/offline; nối monitor/dispatch còn thuộc D10. D11 chạy full test project và probe bắt buộc, lưu riêng kết quả từng runner. Lệnh Android chỉ bổ sung khi M00/M01 khóa toolchain, không phải đầu vào để chạy test Web. Bằng chứng đã chạy của D03 đọc ở báo cáo D03, không dùng lệnh mẫu làm bằng chứng.

## 9 Bảng theo dõi thực hiện

Chỉ sửa trạng thái khi có evidence của bước. Khi test fail, giữ bước đang làm; không nhảy qua. D12 có UAT/release evidence của bản Web; chỉ các bước D là điều kiện hoàn thành đợt một.

| Bước | Kết quả chính | Thực hiện | Test gate | Review CI và evidence |
| --- | --- | --- | --- | --- |
| D00 | Nền source SQL và Web tại tiệm | ĐÃ KIỂM TRA TỰ ĐỘNG, CHỜ THIẾT BỊ | CHƯA ĐỦ ĐIỀU KIỆN | [Evidence và phần còn thiếu](DELIVERY-D00-20261006.md#kết-quả-thực-hiện); chưa review/CI |
| D01 | Khóa nghiệp vụ kiến trúc | ĐÃ TRIỂN KHAI MODEL/CONTRACT | LOCAL POLICY TEST PASS 259/259 | [Contract/kiến trúc/tests](DELIVERY-D01-20261006.md); READY FOR COORDINATOR REVIEW, chưa review/CI; thiết bị D00 vẫn pending |
| D02 | Dữ liệu API và chống trùng | ĐÃ TRIỂN KHAI FOUNDATION | LOCAL SQL/HTTP TEST PASS 29/29; hồi quy 302/302 | [Contract/schema/API/evidence](DELIVERY-D02-20261006.md); READY FOR COORDINATOR REVIEW, recovery plan chờ review độc lập/CI; chưa deploy hoặc nối POS |
| D03 | POS và bill QR giao A5 | ĐÃ TRIỂN KHAI; CÓ GÓI PREVIEW MÁY CODE | LOCAL AUTO PASS 38/38; delivery/security 369/369; schema mới 17/17 + hồi quy 80/80; published Production 2/2; JS 202/202; browser release 8 nhóm; POS 48/48 + bank QR 10/10 từ lượt trước; T05 in giấy pending | [Contract/POS/A5/release/evidence](DELIVERY-D03-20261006.md); preview 7051 đã dừng để chạy F5, helper mặc định 17051; chưa deploy tại tiệm; READY FOR COORDINATOR REVIEW, chưa full gate/review/CI |
| D04 | QR hoặc nhập mã soạn, báo thiếu, thay và duyệt | ĐÃ TRIỂN KHAI; COORDINATOR NGHIỆM THU LOCAL r22; CHỐT TÀI LIỆU r23 | LOCAL ĐẠT: affected798/798, M01+31 guards; Node239/239 có49-input equivalence; browser8+10/A5 đủ8pages; deployment11/11; Production2/2; actual-data business2/2 và whole-migration qualification riêng | [Luồng/API/schema/receipt và giới hạn](DELIVERY-D04-20261006.md), [cổng cuối hiện hành](../../Logs/delivery-roadmap/D04/run-20261007-01/coordinator-final-gates.json); tại lúc chốt nguồn chưa có immutable review/hai full CI, chưa mở D05; in giấy/đầu đọc thật chờ thiết bị |
| D05 | Giữ xuất trả kho và cost | CHƯA THỰC HIỆN | CHƯA CHẠY | Chưa có |
| D06 | Phân công bàn giao xuất phát | CHƯA THỰC HIỆN | CHƯA CHẠY | Chưa có |
| D07 | Nhập hộ kết quả và trở về | CHƯA THỰC HIỆN | CHƯA CHẠY | Chưa có |
| D08 | Bàn giao và chốt mọi quầy | CHƯA THỰC HIỆN | CHƯA CHẠY | Chưa có |
| D09 | Theo dõi giao bù | CHƯA THỰC HIỆN | CHƯA CHẠY | Chưa có |
| D10 | Màn hình văn phòng theo mốc | CHƯA THỰC HIỆN | CHƯA CHẠY | Chưa có |
| D11 | Trọn luồng Web và hồi quy | CHƯA THỰC HIỆN | CHƯA CHẠY | Chưa có |
| D12 | UAT và phát hành Web | CHƯA THỰC HIỆN | CHƯA CHẠY | Chưa có |

Đợt hai không nằm trong tiêu chí PASS của bảng trên:

| Bước | Kết quả chính | Thực hiện | Test gate | Evidence |
| --- | --- | --- | --- | --- |
| M00 | Toolchain APK và kết nối Android | ĐỂ SAU | CHƯA CHẠY | Chưa có |
| M01 | Ứng dụng cập nhật công việc | ĐỂ SAU | CHƯA CHẠY | Chưa có |
| M02 | GPS trong chuyến giao | ĐỂ SAU | CHƯA CHẠY | Chưa có |
| M03 | Kết nối vị trí UAT và release bổ sung | ĐỂ SAU | CHƯA CHẠY | Chưa có |

## 10 Cách tiếp tục qua các lượt làm việc

Bắt đầu ở D00, lần lượt đến D12. Mỗi lượt tiếp tục đọc bảng tiến độ, contract và evidence gần nhất; hoàn thành bước hiện tại, xử lý test lỗi và ghi handoff trước khi xét bước tiếp theo. Không làm lại bước đã có bằng chứng hợp lệ nếu nguồn và dependency liên quan không đổi. Sau D12 có thể vận hành bản Web; khi có nhu cầu triển khai app/GPS mới bắt đầu M00 và không lấy việc chưa có điện thoại để giữ đợt Web chưa hoàn thành.

Khi kết thúc một bước, báo ngắn gọn: đã làm gì, test nào chạy/số đạt, sai lệch tiền/kho nếu có, evidence và bước kế tiếp. Nếu còn test chưa thể chạy, ghi điều kiện còn thiếu cụ thể và giữ cổng chưa đạt. Những quyết định thay đổi tiền/kho/phân quyền phải quay lại contract theo workflow, không tự đổi tiêu chí để vượt test.

Thời lượng đợt Web được ước tính lại sau D00/D01; đợt app/GPS ước tính riêng sau M00. Tiến độ tính bằng số bước qua cổng với bằng chứng. Đợt một hoàn thành triển khai khi D12 nghiệm thu/phát hành đạt; đợt hai hoàn thành khi M03 đạt. D00 còn thiếu kiểm chứng thiết bị; D01 có259 policy tests, D02 có29 kiểm tra SQL/HTTP. D03 đã nối POS vào foundation, form/lookup và phiếu A5/QR. D04 đã triển khai nhận soạn/báo thiếu/thay/duyệt và đạt các cổng local đã nghiệm thu; trạng thái review/CI cuối tra ở biên bản Coordinator liên kết phía trên. D05 **Giữ xuất trả kho và cost** là bước tiếp theo, chưa bắt đầu; D06–D12 chưa thực hiện. Thiết bị/UAT và triển khai tại tiệm vẫn theo các cổng riêng.
