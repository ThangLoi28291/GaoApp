# Kiểm thử toàn hệ thống GaoApp — 23/10/2026

> Đây là kết quả trước lượt sửa lỗi. Tiến độ và kết quả xử lý tiếp theo nằm trong [báo cáo sửa lỗi và kiểm thử lại](SYSTEM-FIXES-20261023.md).

Bản kiểm tra này chạy trên source hiện tại sau phần nâng .NET 10. Chỉ kiểm thử, thu bằng chứng và đánh giá; chưa sửa mã nghiệp vụ, chưa deploy hoặc cài runtime toàn máy.

GaoAppDb được dùng làm nguồn backup COPY_ONLY và đọc metadata. Kiểm tra dữ liệu sâu và thử nâng schema được thực hiện trên bản sao GUID riêng. Không migration, seeder, sửa tài khoản, quyền, menu hoặc giao dịch trên GaoAppDb. Theo lựa chọn của người dùng, ngân hàng, hóa đơn điện tử, AI bên ngoài và máy in thật không được gọi.

Bằng chứng: `.artifacts/system-review-20261023-122803/`. Tổng hợp máy đọc được: `summary.json`; kết quả từng ca: `selected-cases.json`; ca lỗi: `failed-cases.json`.

## Kết quả và cách đọc

**Kết luận: đã hoàn tất lượt kiểm thử nội bộ, vẫn còn lỗi cần xử lý trước production.** Test thất bại không đồng nghĩa mỗi test là một lỗi sản phẩm riêng. Báo cáo tách lỗi đã có nguyên nhân, sai khác nghiệp vụ cần xác minh, dữ liệu chưa thống nhất và vấn đề của bộ test.

| Phạm vi | Kết quả cuối | Diễn giải |
|---|---:|---|
| Build solution và browser runner | 0 lỗi, 0 cảnh báo | Build mới, Release, audit NuGet trực tiếp/gián tiếp bật |
| Backend .NET | 4.637 đạt / 4.749 ca; 112 thất bại | Đủ toàn bộ discovery; 112 tên ca lỗi đều có trong bằng chứng thất bại trước lượt này, cả baseline .NET 8 |
| JavaScript UI | 581 đạt / 588 ca; 7 thất bại | Nhóm giao hàng, báo cáo thực tế/partial/retry |
| JavaScript bổ sung | 76 đạt / 77 ca; 1 thất bại | Assistant mock thiếu Headers; chưa sửa test gốc |
| Browser E2E | 30 đạt / 47 kịch bản; 17 thất bại | 11 kịch bản timeout được chạy lại riêng sau các tác vụ nặng; không bỏ ca thất bại |
| Dữ liệu thật trên bản sao | DBCC và constraints đạt | Có 1 sai khác cấu hình đơn vị; đối chiếu tổng kho không lệch trong phạm vi đã kiểm tra |
| Repository/service đọc dữ liệu thật | 6/6 đạt | Store 1, không ghi dữ liệu; không thay thế kiểm thử HTTP/auth trên host thật |
| Nâng schema bản sao | 79 → 80 đạt | Schema khớp; số dòng/checksum 22 bảng nghiệp vụ, phân quyền và menu giữ nguyên |
| Tải nội bộ | Kịch bản đạt ở 10/20/50 client | Có HTTP 503 bảo vệ báo cáo lợi nhuận khi tải cao; xem chi tiết bên dưới |
| PowerShell | 124 kiểm tra đạt; 1 script kiểm tra bị chặn do thiếu tệp đích | 16 runtime + 18 deployment safety + 11 deployment tools + 67 launcher + 12 assistant paths; parser 86 script không lỗi |
| npm audit | 3/3 dự án Node: 0 cảnh báo lỗ hổng đã biết | Registry chính thức; không cài thêm hay sửa package |

Kết quả backend được hợp nhất theo lần chạy đầy đủ mới nhất có ưu tiên lượt cô lập; không cộng lượt retry vào số ca duy nhất. Các ca backend thất bại bổ sung trong lượt chạy đồng thời đã đạt khi kiểm tra lại. Việc 112 tên ca từng lỗi trước đây không làm giảm mức ưu tiên các lỗi sản phẩm đã tái hiện, cũng không có nghĩa tất cả đều cùng một nguyên nhân.

Build mới solution và browser runner: 0 lỗi, 0 cảnh báo; restore audit cả phụ thuộc trực tiếp/gián tiếp được bật cho solution. Toàn bộ 4.741 mục discovery đã có kết quả; hai theory phân rã lúc chạy nên có 4.749 ca thực thi.

Ba dự án Node được audit là `GaoApp.Tests.Browser`, `tools/browser-assets`, `tools/store-monitor-3d`. Các tệp `package.json` trong `scripts/migration/initial-import` là manifest nhập dữ liệu, không phải package Node. Kết quả audit chỉ phản ánh lỗ hổng đã biết tại thời điểm kiểm tra, không chứng minh toàn bộ ứng dụng không thể bị tấn công.

## 1. Ưu tiên cao — API chốt đơn trả lỗi nhưng tiền mặt đã được ghi

**Tái hiện:** `DeliveryPriceStockCompatibilityTests.P03_Pending_delivery_reserves_an_applied_voucher_blocks_other_POS_and_delivery_then_releases_on_cancellation`. Ca chạy riêng trên CSDL GUID vẫn lỗi. Khi voucher không còn khả dụng vì đang bị giữ bởi đơn giao khác, API `POST /admin/pos/cart/current/payment-and-finalize` trả lỗi nhưng đã có `OrderPayment` tiền mặt 27.000 trong đơn test. Log trung gian ghi 500; middleware sau đó xử lý `BusinessRuleException` thành HTTP 400. Sai lệch cần sửa là khoản thu đã lưu dù chốt đơn thất bại.

**Nguyên nhân đã thấy trong code/log:** `POSController.AddPaymentAndMaybeFinalizeCurrentCart` gọi `AddPaymentToCurrentCartAsync` rồi mới gọi `FinalizeAsync`; bước sau bị từ chối tại `POSService.MarkRewardVouchersAsUsed`. Hai thao tác không nằm trong một ranh giới giao dịch chung ở endpoint này. Log: `diagnostics/focused-voucher-payment.log`; test: `test-results/focused-price-1.trx`.

**Ảnh hưởng:** trạng thái HTTP “lỗi” không phản ánh khoản thu đã lưu. Người vận hành hoặc client có thể hiểu rằng chưa ghi tiền và thao tác lại. Chưa có bằng chứng mất tiền hoặc ghi trùng trong GaoAppDb; ca này là dữ liệu test tổng hợp.

**Phương án đề xuất:** xác minh voucher/trạng thái và xử lý thu tiền mặt + chốt đơn trong một transaction có kiểm tra cạnh tranh; giữ idempotency theo request ID. Nếu thiết kế cho phép tiền được ghi nhưng đơn chưa chốt, API phải trả rõ khoản thu đã nhận và phần chốt thất bại. Không áp dụng rollback mù cho tiền chuyển khoản đã được ngân hàng xác nhận.

**Điều kiện đạt:** voucher bị giữ/hết hạn, đổi trạng thái giữa hai bước, mất phản hồi, gửi lại cùng request và hai quầy đồng thời đều có kết quả tiền/đơn nhất quán, không thu hai lần.

## 2. Ưu tiên cao — đổi đơn vị cơ sở có thể trả HTTP 500

**Tái hiện:** `ProductBaseUnitSqlServerTests.Product_edit_synchronizes_all_base_units_preserves_barcodes_and_rejects_conflicts_and_stale_unit_edits`, ở bước đổi đơn vị hợp lệ của hàng chưa có nghiệp vụ sử dụng.

**Nguyên nhân:** `ProductService.UpdateCoreAsync` sửa `ProductUnitConversion.UnitId`, trong khi thuộc tính này nằm trong một khóa EF. `DetectChanges` ném `InvalidOperationException` khi save. Bằng chứng log đã lưu trong thư mục `diagnostics`, có stack tại `ProductService.cs:416` và `AppDbContext.cs:346`.

**Phương án đề xuất:** thiết kế cập nhật quan hệ trong transaction theo đúng ràng buộc khóa, kiểm tra quyền/store và row version; giữ ID quy đổi và barcode khi yêu cầu nghiệp vụ cho phép. Hàng đã có lịch sử phải được kiểm tra riêng; không xóa rồi tạo lại quy đổi hoặc sửa lịch sử để tránh lỗi EF.

**Điều kiện đạt:** đổi hợp lệ thành công; xung đột được từ chối trước ghi; barcode, quy đổi phụ và lịch sử được bảo toàn; request cũ không ghi đè dữ liệu mới.

## 3. Ưu tiên cao — màn hình thanh toán qua SignalR thiếu phạm vi cửa hàng

**Tái hiện:** các ca `CustomerPaymentDisplaySecurityTests` gọi hợp lệ `BroadcastTerminalEvent` bị lỗi. Log cho thấy `TenantDataScopeException` tại `CustomerPaymentDisplayService.ShiftAsync`, sau khi đi qua `PosSessionHubFilter`.

**Nguyên nhân:** mỗi invocation có DI scope mới. Filter đã xác thực connection/quyền nhưng chưa đặt tenant hợp lệ cho scope dùng để truy vấn trạng thái hiển thị. Chặn truy vấn khi thiếu tenant đang hoạt động đúng; cần sửa nơi khởi tạo scope cho lời gọi hợp lệ.

**Phương án đề xuất:** gắn ngữ cảnh store/terminal từ connection đã được máy chủ xác minh vào invocation scope; reset/dispose đúng vòng đời. Giữ việc xác thực quyền/session mỗi lần gọi và chỉ dựng số tiền/QR từ dữ liệu máy chủ. Không tin StoreId hoặc số tiền trong payload browser, không mở quyền đọc mọi cửa hàng.

**Điều kiện đạt:** preview/ẩn/QR/refresh hợp lệ hoạt động; giả mạo tài chính, khác cửa hàng/terminal, session hoặc quyền bị thu hồi vẫn bị từ chối.

## 4. Lỗi JavaScript khi form có control tên method

**Bằng chứng:** browser customer-debt/customer-deposit ghi nhận `form.method?.toLowerCase is not a function`. Nguồn tại `GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.privacy.js:213`, trong submit listener dùng chung.

Chrome trên HTML tối giản xác nhận `<select name="method">` làm `form.method` trở thành object dù thuộc tính HTML vẫn là `method="post"`. Bằng chứng: `form-method-shadow.json`. Không thể kết luận riêng lỗi listener này đã làm dừng mọi handler thu tiền khác; lỗi JavaScript là xác nhận được.

**Phương án đề xuất:** đọc method/action bằng API thuộc tính DOM phù hợp, không dựa vào named property có thể bị control che. Giữ cơ chế dọn cache khi logout và thêm kiểm tra browser với control tên `method`/`action`.

## 5. Công cụ SchemaInspect không có quyền dữ liệu hệ thống rõ ràng

**Tái hiện:** bản `GaoApp.SchemaInspect` đã build trả `TenantDataScopeException` khi đọc GaoAppDb; chưa migration hoặc sửa dữ liệu. `InspectTenant` chỉ khai báo `IsHostAdmin=true`, trong khi hợp đồng tenant hiện tại yêu cầu quyền đọc/ghi hệ thống rõ ràng cho các truy vấn được guard.

**Đối chứng:** công cụ chẩn đoán chỉ đọc trong artifacts, giới hạn cứng vào clone GUID và khai báo ngữ cảnh CLI hệ thống, đọc được `MatchingAppliedPrefix` ở 79 và `MatchingCurrentSchema` sau nâng clone lên 80.

**Phương án đề xuất:** cấp scope hệ thống theo mục đích cho CLI được tin cậy, rà soát các verifier/repair tool cùng cách tạo tenant. Không nới guard HTTP hoặc coi mọi `IsHostAdmin` là quyền dữ liệu toàn hệ thống. Kiểm tra cả các lệnh từ chối cấu hình/target sai trước khi thực hiện thao tác ghi.

## 6. Các sai khác giá/quà/voucher và giá vốn cần xác minh nghiệp vụ

Các ca giá đã được chạy lại theo từng phương thức trong tiến trình mới: `focused-price-1/2/3.trx`.

- P01 gift: kỳ vọng 20.000, thực tế 30.000; mixed có tổng tiền đúng kỳ vọng nhưng điều kiện `ComboDiscountTotal > 0` không đạt.
- P02 gift: số tiền được duyệt một phần khác kỳ vọng 10.000/15.000.
- S02: hàng nhận lại trước khi có nguồn giá thực tế có `AcceptedReturnedCostText=0`, trong khi test kỳ vọng 5.000; phần giá đã được giải quyết sau receipt có kỳ vọng riêng.

Các kết quả không tự chứng minh giá đã bị tính sai cho khách hàng thật. Cần đối chiếu dữ liệu cấu hình khuyến mãi, điều kiện quà phải có trong giỏ, cách lưu discount và chính sách lấy giá vốn tạm tính. Không sửa số tiền kỳ vọng theo đầu ra đang chạy để làm xanh test.

**Phương án đề xuất:** chốt từng tình huống nghiệp vụ trên fixture riêng, đối chiếu SQL độc lập từ POS → đơn giao → soạn/duyệt → thu/hoàn tiền → giá vốn và ledger. Tách lỗi cấu hình fixture khỏi lỗi tính toán trước khi sửa.

## 7. Dữ liệu GaoAppDb: một mặt hàng có đơn vị cơ sở không thống nhất

Trên bản sao hiện tại, có 1/32.150 quy đổi cơ sở được kiểm tra không khớp `Products.BaseUnitId`: store 1, product/variant `551768`, conversion `70577`, product base unit `1`, conversion unit `2`, factor `1`. SKU chẩn đoán: `banh-keo-moi`. Bản ghi vẫn hoạt động; không có dấu hiệu tên/sku đánh dấu là test trong phép kiểm tra đã chạy.

**Phương án đề xuất:** xác nhận đơn vị nào đúng về nghiệp vụ, kiểm tra các chứng từ đã sử dụng trước khi sửa bản sao và duyệt thao tác dữ liệu thật. Giữ nguyên barcode và lịch sử; không cập nhật hàng loạt mọi dòng về cùng một unit. Cần giải quyết cùng lỗi cập nhật đơn vị ở mục 2 để tránh tạo lại dữ liệu lệch.

## 8. Kết quả kiểm tra dữ liệu sâu và nâng bản sao

- Dữ liệu nguồn đang dùng khoảng 5,8 GB; bảng giao dịch kho có 1.512.669 dòng được đối chiếu.
- DBCC CHECKDB/DATA_PURITY và CHECKCONSTRAINTS trên clone: không trả lỗi. Không có FK/check constraint bị disabled hoặc untrusted trong kết quả kiểm tra.
- Các tham chiếu FK không mang StoreId được kiểm tra chéo tenant: không thấy store khác nhau; các FK scoped còn lại được kiểm tra qua constraint/schema.
- `BeforeQty + QuantityChange = AfterQty` theo precision 4: không lệch trong 1.512.669 giao dịch đang xét.
- Tổng biến động số lượng đối chiếu snapshot hiện tại: 30.457 tổ hợp, không lệch.
- Tổng valuation Amount đối chiếu InventoryValue hiện tại: 30.457 tổ hợp, không lệch.
- Chênh snapshot giá trị 26.055 ở một receipt được giải thích đúng bằng 17 bút toán revaluation; không coi đây là lỗi dữ liệu hoặc mất giá trị.
- Không thấy nhóm idempotency posting bị trùng trong điều kiện kiểm tra.
- Schema clone ở 79 khớp applied prefix. Migrator của gói publish chạy `--schema-only` lên 80, không seed; schema sau đó khớp source hiện tại.
- Số dòng/checksum các bảng nghiệp vụ và menu trước/sau nâng clone giữ nguyên. Đây là phép phát hiện thay đổi bằng checksum, không phải chứng minh mật mã cho mọi trường dữ liệu.

Bằng chứng: `data-integrity.json`, `value-balance-reconciliation.txt`, `valuation-reconciliation.txt`, `clone-schema-before.json`, `clone-schema-after.json`, `clone-upgrade-verification.json`, `clone-business-before/after.txt`.

Sáu phép đọc repository/service trên clone đầy đủ đều đạt: trang sản phẩm, tìm kiếm sản phẩm, trang đơn hàng, tổng hợp bán hàng ngày, lợi nhuận ngày và giới hạn dữ liệu theo store. Lần gọi đầu sản phẩm/đơn hàng khoảng 8–9 giây có chi phí khởi tạo; các phép đọc còn lại khoảng 67–1.567 ms. Chưa dùng các số này để kết luận năng lực phục vụ của host thật. Bằng chứng: `real-data-read-services.json`.

**Tải nội bộ:** dữ liệu tổng hợp riêng, 20 vòng/client ở các mức 10/20/50 client. Đọc đơn và ghi biến động kho đều thành công, số lượng kho khớp và mỗi ghi thành công được áp dụng đúng một lần. Riêng endpoint lợi nhuận: 10 client đạt 20/20; 20 client đạt 26/40, 14 yêu cầu HTTP 503; 50 client đạt 76/100, 24 yêu cầu HTTP 503. Đây là từ chối bận có chủ đích của bộ giới hạn đồng thời, vẫn là ảnh hưởng khả dụng mà người dùng có thể gặp. P95 lợi nhuận khoảng 5 giây ở hai mức cao. Cần tối ưu/đo nhu cầu thực nếu nhiều người mở báo cáo đồng thời; không chỉ gỡ giới hạn để làm hết 503. Kịch bản không bao gồm đăng nhập, truy vấn quyền, browser, SignalR hoặc kết nối ngoài thật. Bằng chứng: `local-load-results.json`.

Lượt gọi Migrator đầu tiên từ thư mục repo bị từ chối vì khóa/PFX nằm trong content root theo cách khởi chạy đó. Chạy từ đúng thư mục gói Migrator, với khóa/PFX ngoài thư mục ứng dụng, đã đạt. Không hạ điều kiện bảo vệ đường dẫn để chạy test.

## 9. Nợ kiểm thử và các kiểm tra không được diễn giải là lỗi sản phẩm

- Nhiều bài còn cố định số migration 74/75/76 hoặc prefix cũ, trong khi source có 80; phải sửa fixture/kỳ vọng theo đúng mục đích, không bỏ chốt downgrade/audit.
- So sánh nguyên HTML bill gặp nonce khác nhau theo response; cần chuẩn hóa riêng nonce và vẫn kiểm tra nonce đúng/chuyển mỗi response.
- Bộ assistant-client mock response thiếu `headers`, làm lỗi khi xử lý 404. Bản sao chẩn đoán chỉ bổ sung mock Headers đúng dạng response thực thì 10/10 test đạt; chưa sửa test gốc.
- Test assistant IIS tham chiếu tệp `Restore-GaoAppAssistantIis.ps1` không còn tồn tại, nên không thể coi là đã kiểm chứng phục hồi IIS. Parser trên 86 script đang có không phát hiện lỗi cú pháp.
- Một số driver browser dùng selector/payload cũ, hoặc tự chèn inline script vào trang đang thực thi CSP; không nới CSP để làm test qua.
- Các timeout khi nhiều bộ test chạy cùng backup/restore đã được chạy lại với mức đồng thời thấp; xem TRX retry trước khi kết luận hiệu năng hoặc lỗi chức năng.

17 kịch bản browser chưa đạt được phân loại để tiếp tục xử lý, không coi tất cả là lỗi nghiệp vụ đã xác nhận:

| Kịch bản | Bằng chứng và bước tiếp theo |
|---|---|
| `customer-debt`, `customer-deposit` | Có lỗi DOM đã xác nhận ở mục 4; ngoài ra còn assertion/timeout cần kiểm tra lại sau sửa |
| `customer-payment-display` | Driver fetch URL tương đối khi chưa có trang làm base; lỗi SignalR máy chủ ở mục 3 được xác nhận độc lập bằng backend |
| `customer-display-receipt` | Driver đọc kết quả regex null; cần đối chiếu HTML/nonce và contract hiện tại |
| `kiosk`, `receiving-mobile` | Script do driver tự chèn bị CSP chặn; giữ CSP, sửa cách driver nạp script đúng nonce rồi đánh giá luồng |
| `delivery-picking`, `delivery-trips`, `pos-scan-feedback` | Assertion về số lượng/trạng thái hoặc vị trí cuộn không khớp; cần xác minh UI và kỳ vọng |
| `delivery-inventory`, `role-permissions`, `pos-receive-shift`, `receipt-document-actions`, `receipt-history`, `receipt-invoice-follow-up`, `receipt-supplier`, `shift-admin` | Timeout vẫn lặp lại khi chạy tuần tự; cần đối chiếu selector, dữ liệu fixture và phản hồi UI trước khi kết luận nguyên nhân |

Tên trong bảng tương ứng cờ CLI có tiền tố `--`. Log gốc là `browser-<tên>.log`; nếu đã retry thì đọc `browser-retry-<tên>.log`. Không sửa selector, nới CSP hoặc thay assertion sản phẩm trong lượt kiểm tra này.

## 10. Điều kiện trước khi triển khai thật

Chưa khuyến nghị production vì các lỗi xác nhận ở trên và regression chưa xanh. GaoAppDb gốc vẫn ở migration 79; việc áp dụng audit migration 80 trên dữ liệu thật cần backup/review và cửa sổ triển khai đã duyệt. Máy hiện dùng runtime toàn máy 10.0.12, thấp hơn floor 10.0.13 của gói; các test dùng SDK/runtime portable 10.0.13. Chưa cài Hosting Bundle hoặc restart IIS/service thật.

Các kết quả nội bộ không thay thế kiểm thử ngân hàng/hóa đơn/máy in hoặc AI provider thật; các kết nối đó được mô phỏng theo lựa chọn của người dùng. Chỉ bắt đầu sửa từng vấn đề sau khi thống nhất phương án tương ứng.

Đã dọn CSDL clone GUID, file backup chứa dữ liệu và chứng thư/khóa giả do lượt kiểm tra tạo; giữ bằng chứng kết quả kiểm thử. Metadata cuối xác nhận GaoAppDb vẫn ONLINE, ở migration 79 và các CSDL có từ trước còn nguyên. Đối chiếu SHA256 của 3.717 tệp source/cấu hình trong bản ghi đầu lượt: 0 tệp thay đổi. Chỉ thêm báo cáo này cùng công cụ/bằng chứng chẩn đoán trong artifacts; chưa sửa mã nghiệp vụ. Bằng chứng: `cleanup-verification.json`, `cleanup-owned-clone.log`, `source-integrity.json`.
