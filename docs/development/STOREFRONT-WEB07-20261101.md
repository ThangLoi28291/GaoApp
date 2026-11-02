# WEB07 — Hủy đơn và theo dõi hoàn tiền

**WEB07 PASS trên snapshot S12: code, test, review độc lập và dọn đúng phạm vi được đồng ý đã đạt.** Sau trả lời “Đồng ý xoá và chuyển bước tiếp theo web08”, đã xóa đúng 1.510 XML, giữ nguyên 1.272 XML thiếu bằng chứng trực tiếp; review độc lập sau xóa PASS. Website chưa mở bán; chưa áp migration vào database cửa hàng. Chỉ tiếp tục WEB08 theo yêu cầu mới, chưa thực hiện WEB09. Bản báo cáo và gate CHỜ trước khi có đồng ý được giữ trong `WEB07/coordinator/cleanup-consented-v1/before-transition` và các seal lịch sử.

## Phạm vi

- W07T01: khách có liên kết riêng tự hủy trước chuẩn bị; từ khi chuẩn bị/giao thì gửi yêu cầu để nhân viên có quyền duyệt hoặc từ chối kèm lý do.
- W07T02: giải phóng giữ thuộc đơn hoặc nhận lại hàng thực tế theo đúng nguồn và hạn mức đã xuất; kiểm tra xung đột chuẩn bị/xuất giao/hủy bằng phiên bản và khóa nghiệp vụ.
- W07T03: theo dõi khoản cần hoàn, số tiền, lý do, người xử lý, trạng thái và bằng chứng thực trả. Yêu cầu và phê duyệt chưa là đã trả tiền; xác nhận thực trả liên kết nguồn chi hiện có đúng một lần. Không tự gửi lệnh chuyển tiền.
- W07T04: tiền QR về sau hủy không mở lại đơn; unknown/timeout không được coi là chưa trả tiền. Hết hạn nhả giữ chỉ sau đối soát đáng tin và kiểm tra nguồn/phiên bản đồng thời.

Hủy toàn bộ và nhận lại đủ hàng theo hợp đồng bản đầu: hoàn khoản khách đã trả còn có thể hoàn, gồm tiền hàng và phí dịch vụ giao. Chi phí thực trả người giao/hãng là khoản riêng; không tự trừ thành phí hủy. Trả hàng sau đã giao, giao một phần hoặc đổi hàng tiếp tục nằm ngoài quy trình hủy của WEB07.

## Quy trình độc lập

Production backend/schema/DI: `storefront_code`; root nhận riêng UI/controller, bốn tệp tích hợp WEB06 và hai tệp RefundService. Oracle và test/fixture/browser: `storefront_tests`. Review mã và bằng chứng: `storefront_review`. Coordinator giữ ranh giới nguồn, xác minh source/binary và tổng hợp cổng cuối. Toàn bộ production của root chịu cùng test/review độc lập; không nhận kiểm tra cú pháp hoặc tự đọc source thành PASS. Có lỗi phải giữ kết quả thất bại, phân tích, sửa bởi đúng vai trò rồi test/review lại.

Oracle v1 đã khóa trước khi tester đọc API mới: `.artifacts/storefront-rollout/WEB07/tests/oracle-v1.md`, SHA256 `4E57E8ACACE4D6C56749E18D0FE0C40C9AD30D5EA2EF034EBBA1BE4D5E126530`. Các mục O01–O33 là yêu cầu bao phủ, không phải số ca đã chạy. Những làm rõ sau mốc này được ghi thành amendment riêng.

## Nền đầu vào

Mốc bắt đầu có 4.168 đường dẫn nguồn và 2.153 dòng trạng thái git; HEAD `4495ffb7ba7cbd0a5d58507a1bf702398d0a8465`. Có tám đường dẫn đã đổi từ sau source WEB06: sáu production và hai test. Before/after hash, bản sao nguồn đầu vào và diff nằm tại `.artifacts/storefront-rollout/WEB07/coordinator/preexisting-drift`. Chúng được giữ làm công việc có trước WEB07, không được gán thành bản sửa hay phê duyệt nghiệp vụ mới trong bước này.

Cụ thể, code xuất giao vật lý đầu vào hiện cho âm tồn khi thực tế đã được duyệt/bàn giao, kể cả khi cấu hình nhận đơn website không cho âm. Cách diễn đạt policy xuyên suốt ở WEB00 và hồ sơ cuối WEB06 khác nền đầu vào này. WEB07 giữ công việc đang có, kiểm thử hành vi hiện tại cùng bảo toàn lượng giữ của đơn khác; không khôi phục source cũ hoặc nhận 219 ca WEB06 cũ là kết quả trên bản mới. Thay đổi sẵn khác gồm converter tiền nguyên VND trên khóa, sentinel cấu hình phí, giá bán đơn giao POS và cấu hình tích hợp hệ thống.

Hồ sơ PASS WEB06 giữ nguyên làm bằng chứng lịch sử. Plan/rollout/state trước khi tiếp tục đã có snapshot; cập nhật các tệp theo dõi không sửa kết quả hoặc archive của WEB06.

## Kết quả kiểm chứng

Chức năng, bằng chứng thực thi và gate dọn đúng XML được đồng ý đã đạt. Bản cuối **APP V8/Test V12**, source snapshot `.artifacts/w7s12`: 59 tệp production, 33 tệp test, 5.219 đầu vào đóng băng. Build thành công, 0 lỗi và 1 cảnh báo xUnit2031 có sẵn. READY r2 có 9.568 kiểm tra nguồn/binary/runtime/phạm vi, drift 0, SHA256 `BDAFBF45F653FA678C7BC444E79331B1F09BB98BFA5C0E262DC642B4EF60B27C`; đã được review độc lập. Mã sản phẩm giữ đúng APP8, SHA256 `D446E52B868F72680B1A78E3344E173ED44971458D6618573E88EE2839059749`. HEAD cuối vẫn `4495ffb7ba7cbd0a5d58507a1bf702398d0a8465`; 92 tệp sản phẩm/test ở checkout và snapshot khớp 184 phép so sánh sau test. Không tạo commit/push trong đợt này.

| Nhóm hiện tại S12 | Kết quả thực |
|---|---|
| 30 ca liên quan 28 lỗi hồi quy và 2 đối chứng | 30 PASS, 0 FAIL, 0 bỏ qua; đúng 30 ID cũ |
| WEB07 — 210 ca | 210 PASS, 0 FAIL, 0 bỏ qua; TRX FA46AB5D…47D951, post9.568 drift0 |
| WEB01–WEB06 — 1.056 ca | 1.056 PASS, 0 FAIL, 0 bỏ qua; đúng 1.056 ID cũ, cả 28 lỗi đã PASS lại |
| Ngân hàng/báo cáo liên quan — 498 ca | 498 PASS, 0 FAIL, 0 bỏ qua; tệp SQL/HTML tạm trở về trạng thái vắng mặt ban đầu |
| Kho/giá vốn/hóa đơn — 157 ca | 157 PASS, 0 FAIL, 0 bỏ qua |
| Hợp đồng WEB00 thuần | 378 lượt assertion PASS, 10 đầu vào không đổi, output riêng |

**Tổng chính: 1.921 lần chạy = 1.921 ID thực khác nhau, 1.921 PASS, 0 FAIL, 0 bỏ qua, 0 trùng.** Reviewer tính độc lập trực tiếp từ đúng bốn TRX hiện tại; không giữ kết quả cũ thay cho lần chạy mới. Nhóm 30, smoke và browser target là lượt chuyển tiếp, không cộng lặp vào tổng chính. WEB00 có 377 nhãn `(Group, Name)` khác nhau vì hai lệnh dùng chung một nhãn; 378 là số lượt assertion thực, không phải số ID TRX. Hai công cụ tổng hợp từng lỗi ở dữ liệu báo cáo dẫn xuất (array scope/Group-Object trên OrderedDictionary); bản lỗi và bản sửa đều giữ lại, nguồn/test/TRX không đổi, tổng cuối được kiểm tra lại trực tiếp từ XML.

| TRX hiện tại | SHA256 |
|---|---|
| WEB07 | `FA46AB5D81DBDA67FF4ED567801336703F9A4ADB4A46E58F2BDC94E22447D951` |
| WEB01–WEB06 | `541368DD09864E15E9493154EA1A023375934728A45BE9BED87F35A194C8E19A` |
| Ngân hàng/báo cáo | `24FF489E7822911495B6451B9D31AAA732D3C9EA4D2F3D3B39DB7DAFDB4CF41E` |
| Kho/giá vốn/hóa đơn | `13A0EC94766CF94940CFE84AE9FA6D53B4B717677744E0A43FA03B05C352A4B2` |

Manifest ID cuối: `.artifacts/storefront-rollout/WEB07/coordinator/current-primary-union-app-v8-test-v12-r2-corrected-v2`; tính độc lập: `review/s12-independent-current-primary-1921-union-review.json`. Nhóm WEB01–WEB06 hiện tại là 170/138/51/207/266/224 ca theo từng bước; 224 WEB06 gồm các kiểm tra có trước WEB07, không gán thành ca mới của tính năng hủy/hoàn.

Các lượt trước giữ nguyên làm bằng chứng lịch sử. S11 APP8/Test11 đã đạt 210/210 WEB07, đồng thời browser target 2/2 với kiểm tra tóm tắt/chi tiết Requested–Approved–Executed và pha hủy. Bộ WEB01–WEB06 S11 đạt 1.028/1.056, có 28 FAIL và không bỏ qua. Phân tích độc lập xác định: 19 setup chèn chứng cứ hoàn chưa đủ điều kiện, 2 setup ép pha hủy bằng SQL, 3 so sánh rollback nhầm ranh giới nhiều migration, 4 test trình duyệt còn giả định giao diện cũ. Test V12 sửa đúng sáu tệp: dùng luồng hủy/hoàn thật và chứng từ chi thực; chuẩn bị ranh giới CORE WEB06 trước khi chụp baseline; đo label liên kết thực của checkbox; thêm đúng hai asset WEB07 vào allowlist có chứng minh GET200/body SHA; đối chiếu thông điệp QR đã xác nhận mà không coi là đã giao. Giữ nguyên tên/attributes, câu DML mục tiêu, mã 51090/51201, số tiền, privacy, ngưỡng 43,9 px và timeout. Cả 28 ID cũ đã PASS lại trong bộ đầy đủ S12; 1.028 ID đã PASS trước đó vẫn PASS.

Lỗi sản phẩm đã sửa gồm phân biệt quét đủ lịch sử với bằng chứng chưa nhận tiền; nhận trọn chứng từ thực chi đúng một lần; bảo vệ nguồn cọc/công nợ; hiển thị rõ khoản chưa tất toán sau từ chối; không tạo hai nghĩa vụ cho một nguồn nhận; UTC trên dòng tiền; HTTP413 cho body quá lớn; JSON object/required fields tại guard SQL; thứ tự index khi Down. QR đóng xóa chi tiết/copy/ảnh thanh toán và chặn callback clipboard cũ. Form ngân hàng đã sửa tràn 320→341 px. Review ảnh sau khi S10 test đạt còn tìm thấy tóm tắt chờ trong khi chi tiết đã trả đủ; APP8 bổ sung trường đọc aggregate `RefundState` và đồng bộ tóm tắt/pha dưới gate token/generation có sẵn, dùng chung nhãn trạng thái, không suy tổng từ 200 dòng hiển thị. Cách xác nhận thực chi được diễn đạt bằng tiếng Việt; phương thức ngân hàng ghi đúng là nhân viên xác nhận chứng từ, không đổi mã lưu hoặc tạo lệnh chuyển tiền.

Browser trong full210 S12 có hai luồng hiện tại đi đủ mười bước; mỗi luồng có 157 bản ghi đo (35 layout, 120 trạng thái nút, 2 keyboard) ở 320/360/390/430/1.440 px, tương phản nhỏ nhất đo được 5,8793. Root đã xem bốn PNG hiện tại và reviewer tám mẫu trong 74 ảnh; tóm tắt/chi tiết cùng hoàn 230.000/230.000, QR đóng, form ngân hàng 320 không tràn. Đủ 20 body asset phản hồi thực khớp source S12, hai lần mất phản hồi có chủ đích mỗi luồng, không lỗi JavaScript. Case/node/TRX, khóa SQL và replay được đối chiếu độc lập. Một lần S9 timeout tải trang 30 giây vẫn chưa xác định nguyên nhân; các lần chạy sau giữ nguyên timeout, không dùng timing mới để suy ngược nguyên nhân cũ.

## Ranh giới nguồn và giới hạn kiểm chứng

Trong full207 ở checkout gốc có hai tệp ngoài phạm vi đổi đồng thời: `AccountController.cs` và `_ReceiptIntake.cshtml`. Khi chuẩn bị S10 tiếp tục có 13 tệp ở mẫu in, POS/menu, script browser in và `GaoApp.Web/Program.cs` đổi ngoài ownership. Đã giữ mọi phiên bản quan sát, không revert. Snapshot giữ các byte baseline bất biến; đúng 15 ngoại lệ nguồn gốc đã được khai báo và review độc lập. 4.254 đường dẫn còn lại vẫn được guard, trong đó có đủ 92 tệp đang sở hữu (59 sản phẩm + 33 test); chỉ expected hash của sáu fixture đã duyệt được cập nhật từ S11. Hồ sơ cũ 4.267 đường dẫn được giữ làm bằng chứng đúng thời điểm trước drift. Không chứng nhận toàn bộ checkout đang đổi hoặc hành vi Admin Main/menu/in mới. Harness quản trị kiểm thử body của chức năng; public host có project và runtime riêng, không chứa assembly quản trị/test/in nhãn.

Chưa áp migration vào database cửa hàng, chưa mở bán và chưa thực hiện UAT ngân hàng/nhà cung cấp hoặc chuyển tiền thật. Ngoại lệ ngày ngân hàng được bao phủ bằng proof-level và nguồn đối soát có kiểm soát; không nhận thành UAT giao dịch thật theo ngày lịch sử. Luồng trả sau giao thành công/giao một phần/đổi hàng tiếp tục ngoài WEB07. COD đã thu mà thiếu chủ thể nhận vẫn giữ nợ bị chặn; không suy chủ thể từ kho/ca/cấu hình hiện tại.

## Tài nguyên và sự cố công cụ

344 XML riêng của các lượt đầu đã được dọn theo phê duyệt cụ thể; không xóa thư mục/log/evidence. Yêu cầu xóa đệ quy ban đầu bị auto-review từ chối trước thực thi. Yêu cầu dọn tiếp 356 XML S4 cũng bị từ chối trước thực thi vì cần đồng ý trực tiếp của người dùng cho xóa không khôi phục được. Không thử lại, chia nhỏ, đổi tên hay đưa việc dọn thủ công vào fixture để né quyết định. Sau khi người dùng đồng ý trực tiếp cho danh sách cuối, script đã kiểm tra lại toàn bộ phạm vi và 15 owner đã kết thúc, rồi xóa đúng 1.510 tệp. Review độc lập trước và sau thao tác đều PASS; 1.272 tệp context-only vẫn giữ nguyên.

Danh sách sau cùng đã rehash chính xác 2.782 XML trên sáu snapshot:

| Phân loại | Số tệp | Byte | Hành động |
|---|---:|---:|---|
| Đủ chứng cứ source + PID/start trực tiếp | 1.510 | 1.533.668 | Đã xóa đúng danh sách sau đồng ý trực tiếp và kiểm tra lại |
| Chỉ có source factory/initial-absence/cohort-time, thiếu PID từng GUID | 1.272 | 1.291.920 | Giữ nguyên, loại khỏi đề nghị xóa |

Manifest được đồng ý: `.artifacts/w7s12/.artifacts/storefront-rollout/WEB07/tests/final-resource-consent-manifest-v1/strict-direct-eligible-exact-files.json`, SHA256 `9279B9AEAC6CB38FF14BD42B24A77E4FD0BD1BBDF16455FC58EAA139DB759BA7`. Inventory giữ lại: `context-only-retained-exact-files.json`, SHA256 `AF30E5272567FBEB8B2268D45750161D86495CC8E173542342ABDC39986EB477`, cùng thư mục. Chỉ xóa exact path/hash/size đã chụp; không xóa thư mục, log, ảnh, source, archive, tệp mới hoặc tệp thay đổi về sau. Đã kiểm tra lại literal absolute path nằm trong vùng snapshot, không reparse, hash và owner đã kết thúc, gồm kiểm tra lại mỗi tệp ngay trước khi xóa. Sự đồng ý này không bao gồm tệp phát sinh về sau.

Hồ sơ sau đồng ý: `coordinator/cleanup-consented-v1/summary.json`, SHA256 `0DF66079A087348BE61B7B9C684BA466242519E58D793CFE5686408C14E70D12`; review độc lập `review/cleanup-final-independent-review.json`, SHA256 `65D77B250EBF119F8B37263514CD767F2A9C926C2CBEBB38B6CCCC3BE286C7C7`. Đủ 1.510 đường dẫn đã vắng mặt; log xóa khớp duy nhất các path/size/hash được duyệt. Đủ 1.272 tệp giữ lại khớp hash/size; mọi thư mục cha và source inventory còn nguyên. Sau xóa, reviewer kiểm tra mới 9.568 nguồn/binary/runtime đều khớp. Hai lần Validate chuẩn bị từng dừng an toàn trước thao tác xóa (CIM không truy cập được trong sandbox; pattern đọc tệp giữ lại WEB03 ban đầu quá hẹp); đã giữ script lỗi, sửa công cụ và review bản cuối trước thực thi.

1.510 tệp đủ chứng cứ trực tiếp là S4/S6/S9/S10 728 + S11 391 + S12 391. S10 đã audit độc lập 353 tệp, bổ sung đúng ba key từ helper WEB06 còn thiếu trong subset350. S11 có 627 tệp context-only, S12 có 645; không chuyển các tệp đó thành chứng cứ PID trực tiếp. Mọi hash/size được kiểm tra mới; chỉ băm và đọc metadata, không phân tích/hiển thị/sao chép nội dung khóa riêng.

Đóng vòng đời thực thi: kiểm tra mới sau test có đủ 9.568 hash khớp, năm tên/trạng thái database nền giữ nguyên, không còn R1 fixture, 25 tiến trình đã ghi nhận của S12 đều kết thúc, hai tệp SQL/HTML cố định trở về trạng thái vắng mặt ban đầu. Kiểm tra tổng hợp lúc 16:07:08 UTC đối chiếu đúng PID/start/name/exe của 17 bản ghi tham chiếu thuộc cả sáu snapshot, tương ứng 16 danh tính tiến trình chuẩn hóa (15 owner XML và một owner TLS riêng); không còn instance khớp đang chạy. Một owner xuất hiện hai lần với cách biểu diễn phần lẻ thời gian khác nhau; không tính thành hai tiến trình. PID tái dùng/không xác định không bị can thiệp. Đủ 48 bộ TLS before/create/cleanup được ghép; truy vấn hiện tại đúng 48 tên CNG/known-user-file lúc 16:03:59 UTC đều vắng mặt, không liệt kê hay mở các khóa khác. Không còn PFX/private-cert file trong tám vùng output đã khai báo. Gate dọn đã đạt với 1.272 XML context-only được giữ có chủ đích; không khẳng định toàn máy không còn XML/khóa hay tiến trình khác.

Một lần chuẩn bị công cụ WEB00 đã ghi 22 cache `bin/obj` cũ do nested props; nguồn và kết quả lịch sử giữ nguyên, không có preimage để khẳng định binary cache cũ bất biến hoặc khôi phục. Các lượt sau kiểm tra sáu output property và chạy riêng; không dùng cache đó làm bằng chứng PASS. Mọi lỗi và lần thử thất bại được giữ cùng bản retest; không đổi kỳ vọng tiền/kho để vượt test.

## Cổng kết luận

- W07T01–W07T04: chức năng/code/test/review độc lập PASS trên S12 APP8/Test12.
- Source/binary/ID union/DB/tiến trình/TLS-CNG-PFX/tệp output: bằng chứng đã đối chiếu; giữ giới hạn 15 source ngoài ownership đang đổi và dữ liệu kiểm soát.
- Gate WEB07: **PASS_WITH_EXPLICIT_CONTEXT_RETENTION** sau đồng ý trực tiếp, xóa đúng 1.510 XML và review độc lập sau xóa. Giữ nguyên 1.272 XML context-only; gate chờ trước đồng ý là bằng chứng lịch sử.
- Người dùng cho tiếp tục WEB08; chưa thực hiện WEB09, mở bán, migration database cửa hàng hoặc giao dịch thật.

## Đường dẫn và quyền

Khách thao tác trong `/shop/orders` bằng liên kết riêng, không cần tài khoản. Nhân viên mở `/admin/storefront/refunds` để xem hàng chờ, quyết định hủy/thu hồi, theo dõi khoản hoàn và xác nhận chứng từ đã thực chi.

- Xem: `storefront.orders.view`.
- Duyệt/từ chối hủy: `storefront.orders.cancel-approve`.
- Xác nhận đối soát giao/hủy: thêm `storefront.orders.reconcile`.
- Duyệt/từ chối khoản hoàn: `storefront.refunds.approve`.
- Xác nhận thực trả/chọn nguồn chi: đồng thời `storefront.refunds.confirm` và `report.treasury.manage`.

Không tự tạo menu. Người dùng tự thêm URL quản trị qua Quản lý menu. Chưa áp migration vào database cửa hàng, giao dịch ngân hàng/nhà cung cấp thật, chuyển tiền hoàn hoặc mở bán. WEB08 được tiếp tục theo yêu cầu mới.
