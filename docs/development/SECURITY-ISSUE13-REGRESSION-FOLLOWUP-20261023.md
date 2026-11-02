# Các lỗi phát hiện khi chạy regression rộng của phần 13

Các kiểm tra nâng nền tảng, gói publish, chuyển tiếp dữ liệu/khóa và smoke Production đã đạt. Regression toàn bộ còn lỗi; không được hiểu kết quả đó là mọi chức năng đều đã được xác nhận. Kết quả từng test và bằng chứng đối chiếu .NET 8 được lưu tại `.artifacts/security-issue13/verification.json`, `latest-dotnet-results.json` và các TRX `baseline8-*`.

Không tự đổi nghiệp vụ hoặc nới kiểm tra quyền để làm xanh các bài này. Phương án dưới đây là đề xuất cho lượt thảo luận/xử lý tiếp theo, chưa được triển khai trong phần nâng .NET.

## 1. SignalR của màn hình thanh toán khách hàng — cần ưu tiên kiểm tra

Ba bài `CustomerPaymentDisplaySecurityTests` lỗi khi gọi `BroadcastTerminalEvent`, tái hiện cả trên assembly .NET 8 trước nâng cấp. Hiện có thể xác nhận lời gọi bị lỗi; chưa đủ bằng chứng kết luận nguyên nhân hoặc suy ra thanh toán/ghi tiền sai.

**Ảnh hưởng:** trạng thái QR và biên nhận trên màn hình khách hàng có thể không cập nhật đúng luồng. Cần giải quyết trước khi xác nhận hệ thống hoạt động đầy đủ.

**Phương án A đề xuất:** tái hiện một ca trên CSDL GUID, đọc log nội bộ để xác định lỗi; kiểm tra ngữ cảnh cửa hàng/terminal trong scope mới của mỗi Hub invocation sau bước xác thực session/quyền. Nếu phải gắn lại tenant, chỉ dùng thông tin connection đã được máy chủ xác minh. Giữ nguyên tenant filter, kiểm tra quyền mỗi lời gọi và nguyên tắc dựng dữ liệu tài chính từ máy chủ. Không bật detailed errors cho production, không tin `StoreId`, số tiền hay tài khoản nhận do browser gửi.

**Điều kiện đạt:** ba ca hiện tại cùng kiểm tra chặn giả mạo, terminal/cửa hàng khác, đổi quyền, thu hồi session và nguồn số tiền đều đạt trên Hub thật.

## 2. Sửa đơn vị hàng hóa và ghi nhận hoạt động ngoài POS

`ProductBaseUnitSqlServerTests` nhận HTTP 500 ở luồng sửa hàng hóa; hai ca `StoreMonitorHttpTests` bị từ chối ở luồng cập nhật/ghi nhận ngoài POS, trong đó có HTTP 409 do thiếu/cũ phiên bản chứng từ. Các ca tương ứng cũng lỗi trên bản .NET 8. Chưa đủ bằng chứng kết luận mọi thao tác thực tế đều lỗi hoặc chỉ fixture sai.

**Ảnh hưởng:** chưa thể xác nhận đầy đủ luồng đổi đơn vị cơ sở, bảo toàn barcode và các sự kiện giám sát ngoài POS.

**Phương án A đề xuất:** tái hiện từng request trên CSDL GUID và thu log exception trong test; đối chiếu model binding, row version, tenant/quyền và hợp đồng request hiện tại. Nếu fixture thiếu dữ liệu bắt buộc thì bổ sung đúng dữ liệu; nếu request hợp lệ vẫn lỗi thì sửa đúng controller/service gây lỗi. Không bỏ kiểm tra cạnh tranh, quyền hoặc đổi HTTP 500 thành thành công giả.

**Điều kiện đạt:** sửa hợp lệ thành công; barcode/đơn vị được bảo toàn; xung đột và row version cũ bị từ chối đúng; monitor phản ánh đúng giao dịch đã commit.

## 3. Giá, hàng tặng, voucher, tồn và workflow giao hàng

Regression phát hiện các sai khác ở `DeliveryPriceStockCompatibilityTests`, `DeliveryD11WorkflowTests` và kiểm tra trạng thái bàn giao. Nhóm tương ứng đã được chạy đối chiếu trên .NET 8; chi tiết ca nào lỗi/đạt xem TRX. Không coi mọi sai khác giá là test cũ chỉ vì cùng lỗi trên runtime trước.

**Ảnh hưởng:** cần làm rõ số tiền được chốt, hàng tặng, giữ voucher, giá vốn và trạng thái hiển thị trước khi xác nhận các luồng giao hàng.

**Phương án A đề xuất:** mỗi ca dùng fixture riêng để loại trừ ảnh hưởng từ các ca dùng chung dữ liệu; chốt kỳ vọng nghiệp vụ rồi đối chiếu giá POS → đơn giao → soạn/bàn giao → thu tiền/hoàn hàng → ledger. Phân biệt ID đơn POS với ID đơn giao và kiểm tra giới hạn số thẻ của snapshot. Không đổi kỳ vọng tiền/tồn chỉ theo kết quả đang chạy.

**Điều kiện đạt:** giá và lượng có đối chiếu SQL độc lập, voucher không dùng hai lần, ghi sổ/thu tiền đúng một lần, trạng thái office và lịch sử đơn đúng.

## 4. Kiểm tra schema và công cụ sửa lịch sử legacy

Nhiều bài vẫn kỳ vọng 74, 75 hoặc 76 migration, hoặc danh sách kết thúc ở các migration cũ; source đã có 80 từ trước nâng .NET. Một số fixture tạo “legacy” trên schema mới nhất khiến script sửa an toàn từ chối lịch sử ngoài danh sách đã duyệt.

**Ảnh hưởng:** bộ kiểm tra schema hiện không xanh; không thể dùng chúng làm bằng chứng hoàn tất đầy đủ cho mọi kịch bản nâng/hạ lịch sử. Không có lý do nới whitelist hoặc bỏ chốt chặn sửa CSDL.

**Phương án A đề xuất:** tách rõ mốc lịch sử đang mô phỏng với inventory source hiện tại, cập nhật danh sách migration được duyệt tại một điểm dùng chung. Fixture legacy phải dừng đúng prefix lịch sử trước khi gây lỗi giả lập. Không thay hàng loạt mọi số 75 thành 80 và không bỏ kiểm tra Down/data preservation.

**Điều kiện đạt:** fresh/current/prefix/corrupt schema, whitelist legacy, rollback khi lỗi và bảo toàn dữ liệu đều đạt; các ca sai schema vẫn bị từ chối trước ghi.

Riêng bước thực tế cần cho dữ liệu local 79→80 đã được kiểm chứng độc lập bằng công cụ chuyển tiếp .NET 8/EF8 → .NET 10/EF10; điều đó không thay thế toàn bộ ma trận lịch sử trên.

## 5. Contract giao diện, fixture JavaScript và so sánh HTML có CSP nonce

Một số contract vẫn dùng danh sách CSS/script, chuỗi nội dung hoặc dạng payload/endpoint cũ. Kiểm tra reprint so sánh nguyên HTML cũng gặp nonce ngẫu nhiên theo từng response. Bộ JS có 8 bài lỗi, gồm các ca UI bàn giao và so sánh tọa độ 3D tuyệt đối.

**Phương án A đề xuất:** đối chiếu UI/API hiện tại với yêu cầu đã chốt rồi cập nhật fixture đúng hợp đồng. Với HTML, chuẩn hóa riêng giá trị nonce khi so sánh nội dung, đồng thời vẫn kiểm tra nonce hợp lệ và thay đổi mỗi response. Với số thực tọa độ, dùng sai số rất nhỏ phù hợp phép tính; không áp dụng cách đó cho tiền hoặc lượng hàng. Giữ các kiểm tra CSRF, text an toàn và dữ liệu máy chủ.

## 6. Fixture tenant, transaction và khóa quan hệ

Một số bài seed nhiều cửa hàng bằng context chưa có scope, mock chưa hỗ trợ transaction mới, hoặc thử sửa trực tiếp thuộc tính thuộc khóa quan hệ. Các lỗi này cũng đã có ở .NET 8.

**Phương án A đề xuất:** seed bằng scope test rõ ràng, chạy phần đọc/ghi bằng đúng store/user; bổ sung mock transaction theo hợp đồng hiện hành; mô phỏng thay đổi quan hệ bằng thao tác hợp lệ với khóa/FK. Không thay đổi guard production, không mở quyền hệ thống cho mọi request và không bỏ kiểm tra vượt cửa hàng.

## 7. Số truy vấn khi tải danh sách đơn hàng

`PosOrderListSqlServerTests` kỳ vọng 5 truy vấn nhưng nhận 6 trên cả .NET 8 và .NET 10; các phần đọc provenance đã có từ trước lượt nâng runtime.

**Phương án A đề xuất:** ghi lại sáu truy vấn và xác nhận truy vấn thứ sáu phục vụ dữ liệu cần thiết, không phát sinh theo số dòng. Nếu cần thì tối ưu batch hoặc cập nhật ngân sách truy vấn với lý do cụ thể; không chỉ tăng số kỳ vọng. Kiểm tra thêm giới hạn trang, tenant và dữ liệu trả về.

## 8. Catalog quyền và trạng thái phiên/terminal trong fixture

`PermissionCatalogDeltaSqlServerTests` kỳ vọng 228 quyền nhưng nhận 222; ca terminal bị khóa kỳ vọng HTTP 403 nhưng thực tế nhận 401; ca đọc lại bill bằng quầy khác nhận 400 khi fixture dùng lại terminal đã bị vô hiệu hóa. Các kết quả lỗi này đều đã tái hiện trên bản .NET 8. Riêng 401/403 đều là từ chối truy cập, không phải bằng chứng vượt quyền.

**Phương án A đề xuất:** đối chiếu quyền theo mã và trạng thái xóa thay vì chỉ tổng số; xác minh catalog delta không tự thay role grants. Chốt hợp đồng 401 cho phiên không hợp lệ và 403 cho phiên hợp lệ thiếu quyền; fixture quầy đọc lại phải có terminal hoạt động riêng. Giữ nguyên kiểm tra session/quyền và không tự tạo menu hoặc tự cấp quyền cho nhân viên.

## 9. Contract bắt ngoại lệ khi audit worker được yêu cầu dừng

`ExceptionSourceContractTests` đánh dấu nhánh `OperationCanceledException` của `AuditOutboxWorker` là catch rỗng. Test cũng lỗi trên .NET 8; chưa có bằng chứng nhánh này làm mất audit đã commit.

**Phương án A đề xuất:** kiểm tra điều kiện cancellation và vòng đời worker; chỉ chấp nhận kết thúc yên lặng khi chính token dừng host đã được hủy. Contract cần phân biệt việc dừng bình thường với nuốt ngoại lệ không mong đợi. Giữ cơ chế lưu audit bền vững, retry và log lỗi thật.

## Chốt triển khai

Phần .NET 10 đã có gói và kiểm chứng riêng, nhưng **chưa thể xác nhận toàn bộ regression xanh hoặc khuyến nghị kích hoạt production chỉ dựa vào các test trọng tâm**. Cần phân loại/xử lý các nhóm trên theo thứ tự đã thống nhất, cài runtime/Hosting Bundle đúng trên host và áp dụng migration audit đã được duyệt với backup trước khi kích hoạt. Bản đang chạy và `GaoAppDb` được giữ nguyên trong lượt chuẩn bị này.
## Phụ lục kết quả đối chiếu cuối cùng

Bộ .NET chạy đủ **4.749 ca: 4.637 đạt, 112 lỗi**. Cả 112 ca lỗi dưới đây cũng thất bại trên assembly .NET 8 trước nâng cấp; không còn ca lỗi chưa đối chiếu. Kết quả này chưa đủ để khuyến nghị production. Bộ JavaScript còn 8/588 ca lỗi riêng.

Tên từng phương thức, thông báo lỗi và đối chứng nằm trong `.artifacts/security-issue13/baseline-comparison.json`; kết quả đầy đủ từng ca nằm trong `selected-case-results.json`.

| Lớp kiểm thử | Số ca lỗi, cũng lỗi trên .NET 8 |
|---|---:|
| `Configuration.AutoInvoiceRoutingMigrationTests` | 1 |
| `Configuration.DatabaseSecurityMetadataTests` | 8 |
| `Configuration.ImportJournalCompatibilityTests` | 1 |
| `Configuration.InventoryPostingMigrationTests` | 2 |
| `Delivery.DeliveryBillLookupPresentationTests` | 1 |
| `Delivery.DeliveryD03BillTests` | 4 |
| `Delivery.DeliveryD03GuardTests` | 1 |
| `Delivery.DeliveryD03SchemaTests` | 3 |
| `Delivery.DeliveryD03SqlServerTests` | 1 |
| `Delivery.DeliveryD04SchemaTests` | 14 |
| `Delivery.DeliveryD04SqlServerTests` | 2 |
| `Delivery.DeliveryD05SchemaTests` | 17 |
| `Delivery.DeliveryD06SchemaTests` | 13 |
| `Delivery.DeliveryD10MonitorTests` | 1 |
| `Delivery.DeliveryD11WorkflowTests` | 5 |
| `Delivery.DeliveryHeldOrderShiftSqlServerTests` | 4 |
| `Delivery.DeliveryOpeningSourceTests` | 1 |
| `Delivery.DeliveryPriceStockCompatibilityTests` | 6 |
| `Inventory.InputInvoiceItemCatalogMappingServiceTests` | 1 |
| `Inventory.InputInvoicePostConfirmLifecycleTests` | 1 |
| `Observability.ExceptionSourceContractTests` | 1 |
| `Purchases.ReceivingManagerApprovalUxContractTests` | 2 |
| `Purchases.ReceivingWorkbenchSqlServerTests` | 1 |
| `Reports.SalesReportReadRepositoryTests` | 4 |
| `Security.CustomerPaymentDisplaySecurityTests` | 3 |
| `Security.CustomerReceiptPrintPreferenceSqlServerTests` | 1 |
| `Security.DeploymentStartupSafetyTests` | 1 |
| `Security.PermissionCatalogDeltaSqlServerTests` | 1 |
| `Security.PosOrderListSqlServerTests` | 1 |
| `Security.ProductBaseUnitSqlServerTests` | 1 |
| `Security.StoreMonitorHttpTests` | 2 |
| `Ui.InventoryLedgerIndexModernUiContractTests` | 1 |
| `Ui.PosCheckoutPaymentUiContractTests` | 1 |
| `Ui.PosPrimeBusinessOwnerContractTests` | 1 |
| `Ui.PosPrimeResponsiveUiContractTests` | 2 |
| `Ui.ProfitReportUiContractTests` | 1 |
| `Ui.WarehouseManagementIndexModernUiContractTests` | 1 |
