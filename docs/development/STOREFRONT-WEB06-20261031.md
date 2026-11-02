# WEB06 — Soạn hàng, giao hàng, đối soát và ghi bán

Trạng thái: **PASS sau code, test và review độc lập; identity và cleanup đã đạt**. Tạm ngưng sau WEB06 theo yêu cầu của chủ cửa hàng; không bắt đầu WEB07. Website chưa mở bán.

Website khách nằm ở project riêng `GaoApp.Storefront.Web`; quản trị nằm trong `GaoApp.Web`, dùng chung lớp nghiệp vụ và kho. WEB06 nối đơn website tới soạn hàng, xuất giao, cập nhật kết quả, nhận lại hàng thực tế, đối soát COD/QR và ghi bán.

## Kết quả triển khai

- Có danh sách, bộ lọc theo trạng thái/cách giao/thanh toán/thiếu tồn và lịch sử người xử lý. Đơn đã giao nhưng chưa ghi bán vẫn được thể hiện riêng.
- Giao gần dùng người giao của cửa hàng; giao toàn quốc lưu hãng, mã vận đơn và kết quả do nhân viên có quyền cập nhật. Đợt này không bắt buộc API hãng vận chuyển.
- Chuẩn bị và xuất giao dùng nguồn đơn website thực, không tạo giỏ, ca hay nhân viên POS giả. Xuất đủ mọi nguồn của đơn trong cùng transaction, giữ đúng kho/chủ hàng/giá vốn và hàng tặng đã chốt.
- Kho âm theo chính sách website; lượng giữ của đơn khác được bảo vệ. Ghi bán tái sử dụng bằng chứng xuất giao, không trừ kho hoặc ghi giá vốn lần hai.
- Tiền khách đã trả, khoản người giao còn phải nộp, tiền cửa hàng thực nhận và khấu trừ có chứng từ được theo dõi riêng. Phí giao khách trả là doanh thu dịch vụ cửa hàng; chi phí giao ghi riêng.
- Ghi bán cần ca thực đang mở của người xử lý, giữ giá và khuyến mãi đã chốt. Thuế/hóa đơn theo chính sách hiện hữu; công việc sau commit có lưu trạng thái để phục hồi mà không tạo lại tiền/kho/hóa đơn.
- Báo giao thất bại không tự nhập kho. Nhận lại hàng theo số lượng thực tế, đúng nguồn và trong hạn mức đã xuất, có chống lặp. Nhận lại hàng không phải bằng chứng đã hoàn tiền; quy trình khách hủy/hoàn tiền thuộc WEB07, chưa bắt đầu.
- Guest tiếp tục xem trạng thái bằng capability riêng trong fragment, không cần tài khoản. Đọc đơn cũ vẫn hoạt động khi cửa hàng tạm dừng nhận đơn mới.

Ví dụ đã đưa vào kiểm thử: hàng 200.000đ + phí giao 30.000đ = khách trả 230.000đ. Hãng nộp 205.000đ và có chứng từ khấu trừ 25.000đ thì hết khoản phải nộp; thiếu chứng từ vẫn còn 25.000đ. Không tự ghi tiền đã thu khách thành tiền mặt cửa hàng, không ghi thêm chi tiền lần hai cho khoản đã khấu trừ.

Đường dẫn quản trị: `/admin/storefront/fulfillment` và `/admin/storefront/service-revenue`. Có kiểm tra quyền ở controller/service. Không tạo hoặc gắn cứng menu; chủ cửa hàng tự thêm đường dẫn qua Quản lý menu.

## Kiểm thử và tính độc lập

`storefront_code` sở hữu production, `storefront_tests` sở hữu oracle/fixture/test, `storefront_review` audit độc lập. Coordinator khóa nguồn, build output riêng, đối chiếu nguồn/DLL/PDB/runtime và chốt cổng. Oracle V1 được ghi trước khi đọc API mới; các điều chỉnh sau đó có phiên bản và ngày riêng.

Candidate cuối là APP V8, 137 đường dẫn, và Test V16, 38 đường dẫn. Build đạt 0 lỗi, có một warning xUnit2031 đã tồn tại ở `InvoiceNotificationTransportTests`. READY có 192 đối chiếu nguồn/test/6 DLL/11 bản dependency runtime không sai lệch; 33 file binary cần thiết được lưu theo hash. Bộ test chọn lọc không chạy `Main`, full bootstrap hoặc database cửa hàng.

| Bộ kiểm tra | Nguồn kết quả cho cổng cuối |
|---|---|
| WEB06 — 219 ID | 219/219 PASS trên APP V8/Test V16, không skip; đúng toàn bộ ID cũ |
| WEB01–WEB05 — 832 ID | 795 PASS trên APP V7/Test V13 +36 PASS trên V14 +1 PASS trên V15; giữ theo phạm vi đã review |
| Ngân hàng/POS/báo cáo — 498 ID | 498/498 PASS trên APP V8/Test V16, không skip; toàn bộ 45 ID lỗi cũ đã đạt |
| Kho/giá vốn/hóa đơn — 157 ID | 157/157 PASS trên APP V8/Test V16, không skip; đúng bốn class đã audit |

Coordinator, tester và reviewer đã tính độc lập theo test ID thực: **1.706 ID distinct đều PASS**, không trùng giữa các nhóm và không skip. Có 874 ID trên APP V8/Test V16 và 832 ID giữ theo phiên bản nêu trên; không tuyên bố toàn bộ 1.706 hoặc 832 ca đều chạy trên bản hiện tại. Sáu TRX chính có 1.744 lượt; 38 lượt đã thay thế không cộng vào coverage. TRX 219 và 498 cũ chỉ dùng đối chiếu danh sách ID/lịch sử, không dùng làm kết quả hiện tại. Bộ WEB06 gồm SQL/service/HTTP/race/browser cùng 17 ca model và 52 ca nhận diện biểu thức schema, không phải 219 luồng end-to-end.

Các kết quả 832 được giữ có giới hạn rõ: hai method production mới chỉ đổi xử lý khóa theo provider và từ chối bank version không hợp lệ. Với SQL Server và rowversion 8 byte hợp lệ, câu khóa/thứ tự/so sánh và nghiệp vụ cũ được giữ nguyên. Đã đối chiếu nguồn, toàn bộ document PDB, ABI tham chiếu và bốn DLL production khác, cùng các lượt 219/498/157 hiện tại. DLL `GaoApp.Web` thay đổi; không dùng nhận định cả năm DLL production còn nguyên byte hoặc mọi method IL giống hệt.

Đối chiếu compile của coordinator: 1.034 document PDB `GaoApp.Web` chỉ đúng hai file đổi, 1.032 document còn nguyên; PDB test từ 755 lên 756 document, chỉ hai file endpoint đổi và thêm một helper, 753 document còn nguyên. Bốn DLL production khác cùng byte; hai reference assembly `ref`/`refint` của Web giữ nguyên. Đây là bằng chứng nguồn/ABI, không thay thế các lượt thực thi hiện tại hoặc tự chứng minh mọi method IL cùng byte.

## Những lỗi đã sửa và bằng chứng trước/sau

- F02: probe độc lập trên APP V1 chấp nhận payment không nguồn sau một native sale đã hoàn tất. Bản sửa ràng buộc đúng nguồn/hạn mức và tính bất biến; cùng SQL trên V3 bị chặn 51200 và rollback giữ nguyên tiền/kho. Các ca guard thực thi tiếp tục kiểm tra trên code cuối. QR dùng GlobalReceipt, COD dùng CodReceipt, khấu trừ dùng chứng từ riêng; không tạo tổ hợp nguồn giả.
- F01: nhận diện manual ambiguity qua các cửa hàng/tài khoản và replay certification được sửa để giữ toàn bộ số tiền quan sát ở Review/confidence3, chặn phân bổ COD hoặc chứng nhận tài sản bổ sung. Số tiền quan sát chưa chứng minh không được tự coi là bank inflow đã xác nhận. Attestation được lưu trước ledger; ledger bền vững trước liên kết certification. Phục hồi sau crash/đổi mapping giữ cùng nguồn gốc, không suy lại tài khoản lịch sử từ cấu hình hiện tại.
- Schema: chỉ nhận diện các cặp CHECK tương đương đã quan sát, giữ test literal/biểu thức gần giống/cờ disabled và untrusted. Down bỏ đúng default 0 thừa gây UPDATE vào snapshot bất biến; không bỏ trigger bảo vệ.
- Lỗi thiếu tồn/trả quá hạn mức được trả đúng status/code; busy 409 và mất phản hồi giữ nguyên request key/body để thử lại, không lặp xuất kho hoặc thu tiền.
- Giao diện tăng tương phản nút, giữ focus/kích thước chạm/giảm chuyển động. Kết quả browser canonical cuối sẽ được ghi theo lượt đầy đủ hiện tại.
- Hồi quy cũ dùng model mới vào schema lịch sử đã được sửa bằng INSERT tham số hóa đúng cột cũ, giữ các giá trị tiền, tồn, menu 719, quyền, định danh và fingerprint thực. Nguồn xuất kho của ca bất biến lấy từ soạn–xuất giao thực, không chèn nguồn thiếu chứng cứ.
- Mã chặn ngân sách 51200 và Down guard 51201 mới được nhận diện đúng message/phạm vi, giữ toàn bộ kiểm tra hậu trạng thái. Một truy vấn cuối sau downgrade dùng sai model được thay bằng đọc cột lịch sử và tăng kiểm tra snapshot; không diễn giải cả chuỗi downgrade nhiều migration là một transaction nguyên khối.
- Hồi quy 498 phát hiện 43 lỗi gọi SQL lock qua InMemory, một lỗi bank rowversion null và một HTTP 500. Bản sửa giới hạn lệnh khóa cho SQL Server, giữ quy tắc manual legacy; native authentication từ chối version thiếu/sai. Hai luồng endpoint dương tính chuyển sang SQL/HTTPS thực, giữ nguyên kiểm tra bảo mật/tiền, không tạo AT hoặc chứng nhận giả. Các vector version null/rỗng/7 byte được kiểm tra âm riêng.

Giữ nguyên TRX lỗi: baseline 832 lần đầu 798 PASS/34 FAIL; retake 37 có 36 PASS/1 FAIL rồi cùng ID còn lại PASS; legacy 498 lần đầu 453 PASS/45 FAIL. Candidate chỉ build/source-review và lỗi parser metadata được tách khỏi lỗi runtime; không sửa lại các lượt thất bại thành PASS.

## Migration, browser và vận hành

Migration `20261101000000_AddStorefrontFulfillmentSettlement` có preflight 51200 và Down guard 51201. Không tự backfill bằng chứng lịch sử. Down từ chối xóa bằng chứng custody/tài chính/hóa đơn hoặc mất precision đã có. Mỗi migration có guard riêng; không coi lỗi ở migration bên trong là bảo đảm toàn chuỗi downgrade chưa thay đổi schema bên ngoài.

Browser canonical của lượt đầy đủ APP V8/Test V16 có hai luồng giao gần/toàn quốc, mỗi luồng 10/10 bước, 36 phép đo DOM và 38 PNG. Đo ở 320/360/390/430/1440px và bàn phím; font nội dung 16px, tương phản tối thiểu khoảng 4,50069:1, không có lỗi contrast/touch target được ghi nhận. Mỗi luồng có một request cố ý mất phản hồi để kiểm tra replay; không ghi thành lỗi ứng dụng. Có 13 vector privacy oracle mỗi report, khác số request thực; SQL DMV phải chứng minh đúng session/chủ khóa/database/resource và CSS được đối chiếu bằng response hash.

Browser quản trị dùng Razor body của module trong host tối thiểu có guard và CSS chức năng thực; guest chạy process riêng. Bảng quản trị nhỏ có vùng cuộn ngang bên trong. Reviewer đã xem tay tám ảnh hiện tại ở năm chiều rộng và cả hai cách giao, chưa thấy blocker mới; không coi đã xem hết 76 ảnh. Đây chưa phải UAT toàn layout/sidebar quản trị hoặc máy người dùng thật.

SQL thử chỉ dùng database GUID có guard trên `.\SQLEXPRESS`. Ngân hàng/hãng giao/transport hóa đơn bên ngoài được kiểm soát; không coi test kỹ thuật là nghiệm thu dịch vụ thật. Hai endpoint hồi quy mới dùng HTTP/HTTPS thật và client pin chứng chỉ synthetic, không giả scheme HTTPS.

TLS Windows dùng khóa synthetic CurrentUser Software KSP có tên GUID, chứng chỉ trong bộ nhớ. Private key tạm tồn tại trong vòng đời test; không cài vào trust store. Cleanup cuối phải xác nhận từng khóa/tệp sở hữu đã hết, không sao chép nội dung khóa.

Cleanup đã xóa đúng 1.515 thư mục GUID sở hữu của đợt thử, 4.812 file gồm 1.649 file khóa XML. Có 3.151 log/metadata an toàn được lưu và đối chiếu lại hash; không sao chép nội dung khóa. Không còn thư mục runtime/media sở hữu thuộc kế hoạch xóa; 14 thư mục WEB01 có sẵn không đối chiếu được thuộc đợt chạy vẫn được giữ nguyên. Mọi đích xóa đã kiểm tra đường dẫn tuyệt đối, phạm vi và reparse point.

Audit TLS cuối dùng 93 bộ before/create/cleanup đã lưu và một khóa chẩn đoán riêng: cả 94 tên khóa CurrentUser và tệp crypto đã biết đều không còn. Không suy số 0 từ việc thư mục runtime đã xóa thành bằng chứng lifecycle; audit này đọc bản metadata lưu riêng. Không cài chứng chỉ vào trust store hoặc duyệt/xóa khóa khác.

Inventory cuối có đúng cùng năm tên/trạng thái database GUID có sẵn, không còn R1 hoặc database thử sở hữu của đợt chạy. Đây là kiểm tra inventory, không phải checksum nội dung database cũ. Không còn tiến trình khớp identity của fixture; đối chiếu PID kèm thời điểm bắt đầu và giữ nguyên PID đã được tiến trình khác tái sử dụng. Không dừng tiến trình không thuộc đợt thử. Hai output SQL/HTML cố định đã được phục hồi đúng trạng thái trước là không tồn tại. Coordinator đối chiếu cuối 192 hash, không sai lệch.

Website chưa mở bán; chưa áp dụng migration vào database cửa hàng, giao dịch ngân hàng thật, chuyển tiền hoàn hoặc UAT chủ cửa hàng. WEB07–WEB09 chưa bắt đầu.

## Bằng chứng

Manifest nguồn/build, TRX từng phiên bản, PDB/ABI closure, ảnh/JSON browser, cleanup và review độc lập được lưu tại `.artifacts/storefront-rollout/WEB06/`.

- [Tóm tắt test cuối](storefront-web06/test-final-app-v8-test-v16.txt).
- [Digest nguồn và bằng chứng](storefront-web06/evidence-sha256-app-v8-test-v16.json): 43 hồ sơ trực tiếp, gồm manifest 189 artifact đã rehash và 76 PNG canonical hiện tại; cổng coordinator sẽ khóa riêng báo cáo/review cuối.
- [Review độc lập cuối — PASS](storefront-web06/review-final-app-v8-test-v16.md).
