# WEB00 — Hợp đồng website bán hàng GaoApp

Ngày: 30/10/2026. Revision: 4, bổ sung yêu cầu trong WEB01 về project/host khách hàng riêng; giữ phí giao `StoreServiceFee` đã chốt. Policy giữ nguyên bản revision 2 đã test và review độc lập. Phạm vi: W00T01–W00T05 trong [kế hoạch triển khai](STOREFRONT-IMPLEMENTATION-PLAN-20261030.md). WEB00 đã hoàn thành; đây là hợp đồng để triển khai WEB01–WEB09, chưa phải website có thể mở bán.

## 1 Kết quả cần bàn giao

Chọn **đơn website độc lập với giỏ và ca POS**. Đặt hàng, giữ hàng và xác nhận QR chạy ngoài ca; giao thực tế, thu tiền và chốt chứng từ là các sự kiện riêng. Chứng từ bán kết nối vào nền GaoApp qua một cầu nối có kiểm soát, sử dụng ca thật khi chốt và tham chiếu lại tiền/kho đã phát sinh. Không tạo ca, quầy, nhân viên hoặc giỏ POS giả.

Các quyết định kỹ thuật bên dưới đã được chọn cho bản đầu. Thông số kinh doanh như bảng phí khu vực, thời hạn QR, tài khoản ngân hàng và thương hiệu được nhập trong cấu hình trước khi mở bán; không lấy số minh họa từ bản mẫu làm mặc định thật.

**Đã chốt phí giao theo cách 1 — `StoreServiceFee`**, theo trả lời “Làm theo cách 1” của chủ cửa hàng: cửa hàng thu phí giao cho cả giao gần và toàn quốc; chi phí trả shipper/hãng ghi riêng. WEB01 lưu lựa chọn này trong cấu hình; bản đầu không bật chế độ thu hộ hãng. `Unconfigured` vẫn là trạng thái bảo vệ nếu thiếu cấu hình thực tế, không còn là quyết định kinh doanh đang chờ. Không suy ra chủ thể nhận tiền từ kho. Trạng thái nghiệm thu và kiểm tra nằm trong [báo cáo WEB00](STOREFRONT-WEB00-RESULT-20261030.md).

WEB00 thêm các hàm quy tắc thuần trong [Domain/Storefront](../../GaoApp.Domain/Storefront/StorefrontContracts.cs). Chúng không truy cập database, gọi ngân hàng, ghi kho, ghi sổ hoặc thay đổi POS. Các `Facts` và bằng chứng truyền vào là dữ liệu do máy chủ đã kiểm tra, không phải DTO nhận trực tiếp từ khách.

## 2 Quyết định kiến trúc và bằng chứng trong nguồn

| Quyết định | Chọn và lý do | Nguồn đã đối chiếu |
|---|---|---|
| ADR00-01 Nguồn đơn | `StorefrontOrder` là gốc cho đơn khách vãng lai. Không mở `Order` giả để lấy khóa ca. `Order.POSShiftId` hiện bắt buộc. | [Order](../../GaoApp.Domain/Entities/Order.cs) |
| ADR00-02 Nguồn giao | Thêm nguồn `Storefront` rõ ràng cho module giao hàng. Mở rộng mô hình nguồn/FK/check/trigger theo loại nguồn; đường POS cũ vẫn bắt buộc ca/quầy/giỏ thật. Không truyền 0 hoặc ID của giỏ khác để vượt ràng buộc. | [DeliveryPersistence](../../GaoApp.Domain/Delivery/DeliveryPersistence.cs), [DeliveryFoundationConfiguration](../../GaoApp.Infrastructure/Data/Configurations/DeliveryFoundationConfiguration.cs), [DeliveryFoundationService](../../GaoApp.Infrastructure/Services/Delivery/DeliveryFoundationService.cs) |
| ADR00-03 Nguồn thanh toán | Tách transport/xác thực/tra cứu/inbox ngân hàng dùng chung, điều phối theo kênh POS hoặc Storefront. Giữ nguyên điều kiện POS. Không gọi toàn bộ `AcbPaymentService.CompleteAsync` cho đơn web. | [AcbPaymentService](../../GaoApp.Web/Services/Acb/AcbPaymentService.cs), [AcbPaymentService.Installments](../../GaoApp.Web/Services/Acb/AcbPaymentService.Installments.cs) |
| ADR00-04 Bằng chứng tiền | Inbox bền vững nhận giao dịch đã xác thực; phân bổ tới đơn trong giao dịch khác có khóa/idempotency. Khoản tiền sai số, đến muộn hoặc chưa ghép được vẫn tồn tại để đối soát. | [AcbPaymentPolicy](../../GaoApp.Web/Services/Acb/AcbPaymentPolicy.cs), [StorefrontPaymentPolicy](../../GaoApp.Domain/Storefront/StorefrontPaymentPolicy.cs) |
| ADR00-05 Chủ thể | Kế thừa chế độ phân bổ kho/chủ thể hiện có; lưu từng lát nguồn thực xuất và giá vốn. Tài khoản nhận tiền phải có ánh xạ chủ sở hữu/quyền thu hộ được cấu hình riêng. `StoreBankAccount` hiện chỉ có cửa hàng, không có đủ thông tin suy ra chủ thể pháp lý. | [StoreBankAccount](../../GaoApp.Domain/Entities/StoreBankAccount.cs), [OrderLegalEntityFinalizeService](../../GaoApp.Application/Services/Orders/OrderLegalEntityFinalizeService.cs) |
| ADR00-06 Chốt bán | Sau giao và đối soát, nhân viên có quyền dùng ca thật đang mở của chính mình để ghi chứng từ qua cầu nối mới. Dùng bằng chứng tiền và phiếu xuất cũ; không xuất lại, không thu lại. `Delivered` có thể chờ ghi sổ; giao diện khách vẫn thấy đã giao. | [POSService.Delivery](../../GaoApp.Application/Services/Orders/POSService.Delivery.cs), [DeliverySettlementPolicy](../../GaoApp.Domain/Delivery/DeliverySettlementPolicy.cs) |
| ADR00-07 Hóa đơn và báo cáo | Giữ quy tắc hóa đơn/thuế và phân bổ chủ thể hiện hành, không phát hành hóa đơn chỉ vì callback tiền. Bổ sung báo cáo nhận tiền web và liên kết loại trùng trước khi bật QR. | [InvoiceCommandService](../../GaoApp.Application/Services/Invoices/InvoiceCommandService.cs), [OperationsReportService](../../GaoApp.Infrastructure/Services/Reports/OperationsReportService.cs) |
| ADR00-08 Giữ hàng | Dùng chung `InventoryReservations` và `InventoryBalance.ReservedQty`, thêm loại nguồn web và tham chiếu chuẩn. Không tạo một sổ giữ hàng thứ hai mà POS không thấy. | [InventoryReservation](../../GaoApp.Domain/Entities/InventoryReservation.cs), [InventoryReservationService](../../GaoApp.Application/Services/Inventory/InventoryReservationService.cs) |
| ADR00-09 Phạm vi bản đầu | Giao đủ một đơn hoặc thu hồi toàn bộ phần đã bàn giao trước khi hủy; chưa hỗ trợ giao một phần, đổi hàng/thay giá sau chốt hoặc trả hàng sau đã giao. Các trường hợp đó cần hợp đồng mở rộng, không tự biến thành nợ khách. | [StorefrontWorkflowPolicy](../../GaoApp.Domain/Storefront/StorefrontWorkflowPolicy.cs) |
| ADR00-10 Host khách hàng riêng | Theo yêu cầu bổ sung của chủ cửa hàng trong WEB01: project/process `GaoApp.Storefront.Web` riêng; cấu hình quản trị tại `GaoApp.Web`; dùng chung lớp nghiệp vụ và dữ liệu. Host khách không tham chiếu Web quản trị, không nạp controller Admin/POS, không chạy migration/seed. | [GaoApp.Storefront.Web](../../GaoApp.Storefront.Web/README.md) |

Một đơn có nhiều kho/chủ thể được chia thành các fulfillment con theo nguồn thật. Mỗi hồ sơ giao vẫn giữ nguồn kho/chủ thể không đổi; nhóm giao của đơn web tập hợp các hồ sơ con, phân bổ giá/giảm giá/receipt theo budget. Chỉ cho parent chuyển Shipping khi toàn bộ phần cần giao đã được bàn giao; chỉ Delivered/chốt khi đủ chứng cứ và đối soát mọi phần. Lỗi giữa các phần được theo dõi riêng và thu hồi phần đã xuất khi hủy, không giả định `FinalizeDeliverySaleAsync` một nguồn hiện tại đã hỗ trợ toàn bộ đơn đa nguồn.

Bản đầu chọn **một transaction ghi xuất cho toàn bộ child fulfillment**, khóa các số dư theo thứ tự ổn định; chỉ commit Shipping khi tất cả xuất/consume/cost/event thành công. Không gọi hãng vận chuyển trong transaction. Projection `PhysicalDispatchRecorded` là OR (bất kỳ phần nào đã xuất/bàn giao), `HasDeliveredGoods` là OR (bất kỳ phần nào đã giao); `AllOrderedGoodsPicked`, `TrustedDeliveryResult`, `AllDispatchedGoodsReturned` và đối soát đủ là AND của toàn bộ phần liên quan. Không bao giờ suy “chưa xuất” từ việc parent chưa lên Shipping. Nếu chứng cứ thực tế có phần đã rời kho nhưng batch ghi sổ thất bại, khóa đơn để nhân viên xử lý sự cố theo chứng cứ, không nhả giữ/hủy như chưa giao hoặc tự nhận lại hàng.

### Những chỗ không thể cắm thẳng vào dịch vụ hiện tại

- QR POS kiểm tra ca/quầy/thu ngân và cờ `HasInputInvoice`; luồng hoàn tất có thể gọi `pos.FinalizeAsync`, in và phát sự kiện. Tách phần dùng chung, không gỡ các kiểm tra của POS để phục vụ web. Việc cho phép QR web cho một mặt hàng phải qua chính sách phương thức đã xác định trên máy chủ; không tự đổi cờ hóa đơn của hàng.
- Giao hàng hiện tạo từ giỏ POS chưa nhận tiền, có `SourceCartId`, `CreatedShiftId`, `CreatedTerminalId` và người tạo; giao trả trước chưa được `DeliverySettlementPolicy` hỗ trợ (`AdvanceAmount == 0`). WEB05/WEB06 phải bổ sung adapter trả trước có chứng cứ, không nhét tiền QR vào trường cash hoặc gọi lại xác nhận chuyển khoản thủ công.
- `FinalizeDeliverySaleAsync` hiện tạo `OrderPayment` và gọi `ApplyPaymentsToShift`. Cầu nối web cần cách **phân bổ khoản đã nhận**, không coi tiền ngân hàng từ hôm trước là lần thu mới của ca hôm nay.
- Báo cáo dòng tiền hiện chỉ lấy `OrderPayment` của đơn hoàn tất/hoàn và lấy ngày muộn hơn giữa trả tiền/chốt đơn; cộng thêm `TreasuryEntries` riêng. Nếu tạo phiếu thu lúc callback rồi giữ nguyên phép cộng này khi chốt, tiền sẽ bị đếm hai lần. WEB05/WEB06 phải cập nhật truy vấn theo khóa nguồn bền vững, không loại trùng bằng nội dung ghi chú.
- Đơn web chạy qua tenant đã xác định trên host trong [TenantResolutionMiddleware](../../GaoApp.Web/Middlewares/TenantResolutionMiddleware.cs). Client không được tự gửi `StoreId`, chủ thể hay tài khoản nhận tiền để đổi phạm vi.

### Định danh QR ngân hàng ngoài ca

Cấu hình web phải có định danh terminal/user **đã đăng ký với ngân hàng** và merchant/account tương ứng, độc lập với `POSTerminalId`, `POSShiftId` hay tài khoản nhân viên. Lưu snapshot các định danh ngân hàng trong intent trước khi gửi request. Không tự lấy quầy gần nhất, quầy đã đóng hoặc tạo một nhân viên hệ thống để lấp trường provider.

Theo transport hiện tại trong `AcbPaymentService`, số QR phải nguyên dương và không vượt `int.MaxValue`, MerchantId và BeneficiaryName tối đa 30 ký tự, provider orderId tối đa 13 ký tự. WEB01/WEB05 phải validate các giới hạn này khi bật QR và trước checkout; giỏ vượt giới hạn QR vẫn có thể chọn COD hợp lệ, không ép kiểu tràn hoặc tự chia nhiều khoản trong bản đầu. Định danh web đề xuất được khóa dạng `W{globalCorrelationSequence:D12}` (13 ký tự, sequence không vượt 999999999999), dùng registry unique bền vững để phân biệt namespace POS `GA...`; không tạo bằng cắt chuỗi OrderId hoặc dựa trên số ID trùng giữa hai bảng. Hết namespace thì chặn và cảnh báo cấu hình, không quay vòng. Cùng provider reference luôn trỏ lại một intent/kênh/cửa hàng. Khả năng ngân hàng chấp nhận định danh kênh web phải được kiểm chứng ở WEB05; thiếu đăng ký hoặc chưa kiểm chứng thì QR chưa mở.

### Đường đi chuẩn

```text
Giỏ khách -> báo giá có phiên bản -> đặt đơn + giữ hàng + command receipt
  COD -> Chờ chuẩn bị
  QR  -> Chờ thanh toán -> inbox tiền đã xác thực -> phân bổ đúng đơn -> Chờ chuẩn bị
Chờ chuẩn bị -> Chuẩn bị -> bàn giao + xuất kho + snapshot giá vốn -> Đang giao
Đang giao -> bằng chứng giao -> Đã giao
Đã giao + hàng/tiền đã đối soát -> ca thật + cầu nối ghi bán -> Hoàn tất ghi sổ
```

Thông báo/email nếu bổ sung chạy qua outbox sau commit; gửi lỗi không làm mất đơn. Không gửi tin nhắn thật trong WEB00.

## 3 Hợp đồng dữ liệu cho WEB01

Tên dưới đây là tên logic đã chọn; WEB01 đặt cấu hình EF/migration tương ứng theo chuẩn của dự án, không đổi ý nghĩa để né ràng buộc. Tất cả quan hệ đều bao gồm `StoreId`, không chỉ FK một ID.

| Nhóm dữ liệu | Nội dung bắt buộc | Khóa/ràng buộc cần có |
|---|---|---|
| StorefrontSettings | Bật bán, thương hiệu, nguồn kho/chủ thể, COD/QR, bán âm, hạn giữ QR, cách phân loại phí ship | Một cấu hình/cửa hàng; mặc định đóng; số cấu hình tăng revision; cấu hình thiếu chặn mở |
| StorefrontProduct / Variant / SellingUnit | Tham chiếu hàng gốc; published mặc định false; nội dung/ảnh/thứ tự; kg mỗi đơn vị bán | Một bản xuất bản mỗi đối tượng/cửa hàng; nguồn phải cùng cửa hàng, còn hoạt động và được bán |
| StorefrontShippingZone / Rule | Mã địa giới chuẩn, nội bộ/toàn quốc, thứ tự vùng, bảng phí cố định/kg, hạn chế và thời gian giao | Một luật được chọn xác định; vùng ưu tiên không mơ hồ; revision; thiếu vùng trả unavailable |
| StorefrontQuote | Hàng, biến thể/đơn vị, giá/ưu đãi, kg, địa chỉ chuẩn, vùng, phí và tổng; expires/revision/fingerprint | Token báo giá ký hoặc lưu máy chủ; StoreId lấy từ tenant; client không sửa số tiền |
| StorefrontOrder / Line | Mã đơn, guest token hash, recipient, địa chỉ, snapshot báo giá, phương thức, thời điểm, bốn trục trạng thái | Mã đơn unique/cửa hàng; token ngẫu nhiên mạnh và lưu hash; rowversion; tổng hàng/ship tách riêng |
| StorefrontCommandReceipt / Event / Outbox | ClientRequestId, loại lệnh, payload hash, kết quả, actor, revision, causation | Unique(StoreId, loại lệnh, requestId); cùng khóa khác payload trả conflict; lịch sử không sửa/xóa |
| StorefrontPaymentIntent | OrderId, số phải thu, tài khoản, chủ thể nhận/quyền thu hộ, provider reference, hạn | Một intent hiệu lực/đơn; mã provider riêng toàn kênh; không dùng OrderId web để đoán ID POS |
| BankReceipt / PaymentAllocation | Provider/account/transaction ID, thời điểm thực nhận, raw evidence được bảo vệ, amount, owner; phân bổ tới đơn/refund | Unique(provider, receiving account, external transaction ID); cùng ID khác số/phạm vi -> review; allocation có budget/unique source |
| InventoryReservations (mở rộng) | ReferenceType Storefront, ID đơn/dòng, nguồn kho/chủ thể, base quantity, trạng thái | Có FK/kiểm tra phạm vi; khóa chung theo số dư; reserve/release/consume chỉ đúng owner |
| DeliveryOrder (mở rộng) / Origin | Loại nguồn POS/Storefront, nguồn web hoặc giỏ POS, kho/chủ thể thực, actor thật khi nhân viên thao tác | Chính xác một nguồn; bắt buộc origin POS cũ theo điều kiện; mã tra cứu không dùng chung guest token |
| StorefrontSettlementLink | Đơn web, phiếu giao/xuất, receipt allocations, chứng từ bán, ca/người chốt, fee/cost splits | Unique một lần chốt/đơn; unique nguồn tiền và xuất; có thể nhiều owner slices, không gộp sai chủ thể |
| StorefrontRefund / RefundEvidence | Lý do, số tiền, phần hàng/ship, trạng thái duyệt/thực trả, người duyệt, bằng chứng, nguồn tiền | Tổng hoàn không vượt receipt còn lại; external payout unique; không tự gọi chuyển tiền |

Số tiền tổng là VND nguyên theo quy tắc làm tròn hiện tại. Số lượng tối đa bốn số lẻ theo đơn vị kho; hệ số quy đổi và kg giữ độ chính xác riêng, không làm tròn kg từng dòng thành kg nguyên. Lưu thời gian UTC; ngày kinh doanh dùng cùng chính sách múi giờ hiện có của GaoApp. WEB00 chưa tạo bảng hoặc migration.

## 4 Trạng thái, quyền và hiệu ứng

### 4.1 Bốn trục độc lập

| Trục | Giá trị và ý nghĩa |
|---|---|
| OrderPhase | `AwaitingPayment` chờ QR; `AwaitingPreparation` đã tiếp nhận; `Preparing`; `Shipping`; `Delivered`; `Completed` đã ghi sổ; `Cancelled` kết thúc hủy |
| Payment | Unpaid/Pending/Confirmed/Review. Confirmed chỉ khi đủ tiền khách đã trả có chứng cứ. COD có thể Confirmed nhưng hãng chưa bàn giao tiền về cửa hàng. Review có thể cùng tồn tại số tiền đã nhận, không xóa khoản thu. |
| Delivery | NotPlanned/Ready/Picking/HandedOver/Delivered/ReturnPending/Returned. Chuyển theo chứng cứ vật lý; callback ngân hàng không chuyển trục này. |
| Refund | None/Requested/Approved/Executed/Rejected. Requested/Approved chưa là đã chi tiền; Executed phải có bằng chứng thực trả và ngân sách còn lại. Hủy đơn không đồng nghĩa đã hoàn tiền. |

Tên Payment/Delivery/Refund ở bảng là nghĩa hợp đồng, đã được WEB01 ánh xạ sang enum/schema nền. Các projection `Facts` trong Domain tiếp tục thể hiện điều kiện liên quan của quy tắc thuần. `Completed` phía nội bộ không làm khách phải xác nhận đã nhận lần hai. Đơn đã hủy không được tự mở lại bởi tiền đến muộn.

### 4.2 Ma trận chuyển trạng thái của bản đầu

Mọi lệnh phải đúng cửa hàng và `expectedRevision`. Nhân viên còn phải qua quyền controller/service theo dữ liệu quyền hiện có; quyền trong Domain là kiểm tra thứ hai. Guest chỉ truy cập đúng đơn với token riêng đã xác thực.

| Lệnh/sự kiện | Từ -> đến | Điều kiện | Hiệu ứng được phép |
|---|---|---|---|
| Đặt COD | Mới -> AwaitingPreparation | Quote còn hợp lệ, hàng/địa chỉ/quyền bán hợp lệ | Tạo một đơn, giữ hàng; chưa thu tiền/chưa xuất |
| Đặt QR | Mới -> AwaitingPayment | Như trên, tài khoản/cấu hình QR sẵn sàng | Tạo đơn và giữ; tạo intent qua outbox; chưa thu/chưa xuất |
| Receipt đúng | AwaitingPayment -> AwaitingPreparation | Bằng chứng xác thực, đúng store/order/intent/account/owner, đủ chính xác số phải thu | Xác nhận thanh toán tự động, không chốt bán hoặc giao |
| StartPreparation | AwaitingPreparation -> Preparing | Staff Prepare; không có yêu cầu hủy; QR phải Confirmed, không Review | Bắt đầu soạn; chưa xuất |
| Dispatch | Preparing -> Shipping | Staff Dispatch; đủ hàng toàn đơn, có bàn giao thật; không yêu cầu hủy; tiền đủ điều kiện | Tiêu thụ giữ của đơn + một lần xuất; lưu giá vốn/slices |
| RecordDelivery | Shipping -> Delivered | Staff RecordDelivery, đã xuất và bằng chứng giao tin cậy; chưa chấp thuận thu hồi | Đánh dấu giao. Nếu yêu cầu hủy đến cùng lúc, giữ audit và chuyển kiểm tra; không tự hoàn tiền |
| SelfCancel | AwaitingPayment/AwaitingPreparation -> Cancelled | Guest có token; chưa xuất; khóa phiên bản | Giải phóng giữ của đơn; có tiền thì tạo refund review |
| RequestCancellation | Preparing/Shipping -> giữ trạng thái | Guest có token, chưa giao, chưa chấp thuận thu hồi | Tạo yêu cầu; chặn bàn giao mới khi đang đợi duyệt |
| ApproveCancellation trước xuất | Preparing -> Cancelled | Staff ApproveCancellation; có yêu cầu; chưa giao/xuất | Nhả giữ; có tiền -> refund review |
| ApproveCancellation sau xuất | Shipping -> Shipping + ReturnPending | Staff ApproveCancellation; có yêu cầu; chưa giao | Yêu cầu thu hồi; chưa nhập kho/chưa đánh dấu đã hủy |
| ReceiveReturn | Shipping + ReturnPending -> Cancelled | Staff ReceiveReturn; chứng cứ nhận đủ hàng đã xuất, chưa giao khách | Một lần nhập trả đúng ngân sách; hoàn theo review nếu có tiền |
| ConfirmRefund | Cancelled -> Cancelled | Staff ConfirmRefund; yêu cầu còn mở; thực trả đã xác thực và khớp ngân sách/đối soát | Xác nhận đã hoàn; không gửi lệnh trả tiền |
| ExpireUnpaidQr | AwaitingPayment -> Cancelled | System ProcessBankEvents; hết hạn; ngân hàng xác nhận không nhận; không Review/có tiền | Nhả giữ. Nếu kết quả không rõ thì giữ đơn/giữ hàng và đưa vào kiểm tra |
| PostSale | Delivered -> Completed | Staff PostSale; hàng/tiền đối soát, đã trả đủ, không hủy/hoàn treo; ca thật mở đúng store và người chốt | Ghi bán/hóa đơn theo chính sách hiện hữu qua bridge; không xuất/thu lần hai |

Nhân viên từ chối yêu cầu hủy phải ghi lý do và event, xóa cờ pending trong giao dịch có phiên bản; hành động này không thay pha đơn hoặc tiền/kho. Sự kiện giao thất bại được xác thực tạo yêu cầu thu hồi có lý do của nhân viên, đi cùng quy trình nhận lại hàng; không giả mạo khách đã bấm hủy. Retry trả kết quả command receipt, không chạy hiệu ứng lại.

### 4.3 Chống tranh chấp và giả mạo

1. Checkout: khóa idempotency theo store/request trước khi revalidate; một transaction ghi đơn, giữ hàng, snapshot và outbox. Cùng request khác nội dung -> 409. Nhấn lại/retry không tạo đơn thứ hai.
2. Mọi command trên đơn: khóa aggregate và kiểm tra rowversion. Dùng cùng thứ tự khóa kho/chủ thể/variant để giảm deadlock; retry toàn command qua receipt, không retry riêng phần ghi tiền/kho.
3. Callback: xác thực theo provider; ghi inbox trước; khóa unique external transaction ở phạm vi cả hai kênh. Chỉ dispatcher đáng tin chọn target, không tin OrderId/StoreId từ callback chưa xác minh.
4. Callback và hết hạn/hủy cùng tranh một phiên bản đơn. Callback thắng -> không hết hạn; hủy thắng -> tiền được giữ để hoàn/đối soát, không mở lại. Unknown không suy thành chưa thanh toán.
5. Guest token đủ 256 bit ngẫu nhiên, lưu hash, không dùng mã đơn/số điện thoại làm bí mật. Tra cứu khôi phục bằng số điện thoại cần OTP hoặc kênh xác minh riêng ở WEB04; bản đầu dùng liên kết riêng được cấp khi đặt. Giới hạn tốc độ, CSRF phù hợp, validation và che dữ liệu nhạy cảm; token không vào log/referrer/analytics.

Review tiền là cờ giữ cho đến khi có quyết định xử lý được phân quyền và lưu audit. `Evaluate` bắt buộc nhận `hasUnresolvedReceiptReview` từ projection máy chủ. Ví dụ đã nhận sai 100.000/230.000 rồi nhận thêm đúng 230.000: giữ cả hai khoản và tiếp tục Review, không tự xác nhận đủ điều kiện soạn vì khoản đầu chưa được xử lý. Duplicate không ghi thêm tiền và không được xóa cờ Review. Chỉ quy trình giải quyết review có quyền mới được xóa cờ, sau khi đối soát toàn bộ tiền cũ; không có thao tác khách tự mở khóa.

## 5 Tiền, phí ship, COD và ghi sổ

### 5.1 Đẳng thức và cách phân loại phí đã chọn

`Tổng khách trả = Tiền hàng ròng đã làm tròn + Phí giao khách trả`.

| Chế độ | Hàng 200.000, khách trả ship 30.000, phí hãng thực tế 25.000 | Cách ghi |
|---|---|---|
| StoreServiceFee — đã chọn cho cả hai hình thức giao | Tổng khách trả 230.000; hàng 200.000; dịch vụ giao 30.000; chi phí giao riêng 25.000 | Phí khách trả là khoản dịch vụ cửa hàng. Chi phí giao có chứng từ riêng, không giảm âm doanh thu hàng. Không tạo SKU giả để lẫn phí vào giá vốn hàng. |
| CarrierPassThrough — không bật trong bản đầu | Tổng khách trả 230.000; hàng 200.000; khoản thu hộ 30.000 | Policy có hỗ trợ để kiểm soát nghĩa tiền, nhưng đây không phải lựa chọn của cửa hàng. Không tự chuyển giao toàn quốc sang chế độ này. |
| Unconfigured — bảo vệ cấu hình thiếu | Không có phép ghi sổ ngầm | WEB01 phải lưu StoreServiceFee đã chốt trước khi mở bán. |

`StorefrontFinancialPolicy.Split` phân tách các khoản, không tự quyết định VAT hoặc bút toán hóa đơn. Với thu hộ, `SeparateDeliveryExpense=0`; số phí hãng được xác minh đi vào `CarrierPayableSettlement`, chênh lệch tiền thu hộ trừ phí hãng đi vào `CarrierPayableDifference`. Chênh lệch dương hoặc âm đều bật `RequiresFeeReview`, không tự ghi lợi nhuận/chi phí của cửa hàng; đây là phân loại nghĩa vụ, chưa phải bằng chứng đã chi tiền. Phí/thuế dịch vụ vẫn cần tài khoản, chủ thể và cấu hình hợp lệ trong adapter. Khoản chi đã dùng làm chi phí phải có source link để không vừa trừ net remittance vừa tạo thêm chi tiền.

### 5.2 Ánh xạ theo sự kiện

| Sự kiện | Tiền thật / số dư | Bán hàng, hóa đơn và giá vốn |
|---|---|---|
| Nhận đơn | Không thu, không ghi tiền vào ca | Không doanh thu; chỉ snapshot giá và giữ |
| QR nhận đúng tiền | Receipt vào tài khoản thực, theo `OccurredAtUtc`; phân bổ cho khách trả trước/đơn | Chưa tự hoàn tất bán/giao; không chạy POS finalize |
| QR sai số/tiền thêm | Giữ toàn bộ thực nhận ở inbox/đối soát; không cắt còn đúng số phải thu | Payment Review, chặn chuẩn bị/giao nếu chưa được giải quyết; không bỏ phần thừa |
| COD khách trả người giao | Xác nhận tiền khách đã trả, khoản phải thu từ người/hãng giao | Không ghi tăng tiền mặt cửa hàng khi tiền còn ở người giao; không ghi nợ lại cho khách đã trả |
| COD bàn giao/đối soát | Tiền cửa hàng thực nhận + khấu trừ có chứng cứ; còn thiếu là công nợ người/hãng giao | Chưa chốt khi chưa giải quyết chênh lệch. Net remittance không được biến phần khấu trừ thành chi tiền lần hai |
| Xuất giao | Chưa tự ghi nhận khoản thu mới | Typed inventory posting ghi xuất, giá vốn và nguồn thực; tồn âm có giá vốn tạm theo cơ chế hiện có |
| Giao thành công | Giao hàng và thanh toán độc lập | `Delivered`; chờ bridge ghi bán nếu chưa có ca/đối soát |
| Chốt bán | Phân bổ receipt đã có; thu mới bằng 0. COD tiền mặt bàn giao đúng ca thật thì liên kết một lần | `Order` chứng từ bán tiền hàng; phần dịch vụ/thu hộ tách theo cấu hình; reuse phiếu xuất/cost, không xuất lần hai; hóa đơn sau commit theo policy hiện có |
| Hủy trước xuất | Nhả giữ, không tạo chi tiền vì đổi trạng thái | Chưa có bán để đảo; đã nhận tiền tạo yêu cầu hoàn |
| Nhận hàng thu hồi | Chỉ nhập trả lượng thực nhận, không tự khôi phục bằng số đặt | Cost reversal theo lát xuất nguồn; không tạo doanh thu âm khi chưa chốt bán |
| Thực trả hoàn | Ghi chi đúng nguồn, thời điểm, tài khoản/người thực trả, số thực trả | Liên kết refund evidence; không suy từ Requested sang Executed |

Ví dụ COD nội bộ: khách trả 230.000, nhân viên chưa giao tiền -> CustomerPaid=true, StoreRemitted=0, Outstanding=230.000, chưa đủ điều kiện chốt. Giao đủ 230.000 -> Outstanding=0 và cho chốt sau đối soát hàng.

Ví dụ COD hãng: khách trả 230.000; hãng chuyển 205.000 và có khấu trừ phí đã xác minh 25.000 -> Outstanding=0, ReadyForFinancialSettlement=true. Chỉ khai đã chuyển 205.000 mà chưa có chứng cứ khấu trừ -> còn thiếu 25.000. WEB06 cần màn hình đối soát và source link riêng cho khấu trừ, không tin số phí do client tự gửi. Khấu trừ chưa rõ chủ thể hoặc phí vượt budget chuyển review.

Ví dụ QR hủy: nhận 230.000 rồi khách hủy trước soạn -> đơn Cancelled, giữ hàng về 0, thực nhận vẫn 230.000, refund Requested. Sau khi nhân viên xác minh đã trả 230.000 -> Executed; tổng tiền hoàn không vượt tiền nhận chưa hoàn. Tiền về sau hủy cũng theo quy tắc này, không tạo lại đơn.

### 5.3 Chủ thể, ca, báo cáo và hóa đơn

- Nguồn kho/chủ thể kế thừa bộ phân bổ hiện có; không ép toàn bộ đơn vào chủ thể của tài khoản ngân hàng. Mỗi receipt lưu bên nhận và quan hệ thu hộ đã được cấu hình cho tất cả nguồn hàng; thiếu ánh xạ thì QR chưa được bật.
- Bản đầu không tự chọn chế độ sổ mới. Cầu nối kế thừa chế độ GaoApp đang cấu hình, ngày chốt bán là ngày ca thật thực hiện; ngày nhận tiền, ngày xuất, ngày giao và ngày chốt đều lưu riêng. Đơn Delivered chưa ghi sổ phải có danh sách cảnh báo/bàn giao, không biến mất khỏi vận hành.
- Nếu một đơn có nhiều chủ thể, bridge ghi các phân bổ/chứng từ theo chế độ hiện tại, khóa tổng hàng và tổng tiền giữa các chứng từ. Phí ship có chủ thể/đối tượng hưởng riêng, không chia theo tồn kho một cách suy đoán.
- Thu tiền ngoài ca phải xuất hiện trong dòng tiền ngân hàng ở ngày thực nhận, kể cả đơn chưa chốt. Báo cáo bán tiếp tục theo chứng từ bán hiện có. Khi link sang OrderPayment, báo cáo dòng tiền loại bản mirror đã có receipt bằng FK/unique link. Ca không được cộng lại tiền ngân hàng đã nhận ngoài ca; cash bàn giao ca chỉ có một chứng từ tăng quỹ.
- Tách receipt thật, receivable người giao, thu hộ hãng, doanh thu hàng, phí dịch vụ, chi phí giao và refund trong truy vấn và export. Tổng đầu kỳ + thu - chi = cuối kỳ; phân bổ receipt không phải lần thu mới.
- Quy tắc `HasInputInvoice`/route hóa đơn hiện tại vẫn do module hóa đơn xử lý. WEB00 không thay đổi chính sách thuế/hóa đơn hoặc tự cho phép một loại hàng bằng cách sửa cờ. Phải kiểm tra eligibility của phương thức QR trước khi khách đặt; nếu chưa có policy web được phép theo cấu hình hiện hữu thì không quảng bá QR cho giỏ đó.

## 6 Hàng đăng, giá, kg và giữ hàng

### 6.1 Xuất bản và giá bán lẻ

- Hàng/biến thể/đơn vị phải cùng cửa hàng, hoạt động, `IsSellable` và được chọn đăng web. Website mặc định tắt, mọi sản phẩm mặc định chưa đăng. Không dùng `IsSellable=false` để chỉ ẩn web vì sẽ ảnh hưởng POS.
- Catalog, URL trực tiếp, tìm kiếm, báo giá và đặt đơn dùng cùng predicate xuất bản. Tắt đăng không làm mất snapshot của đơn đã nhận, nhưng chặn đặt mới từ giỏ cũ.
- Giá cơ sở dùng `ProductSalePricePolicy.Pos(variant, unit, wholesale:false)` để khớp POS: override đơn vị dương -> biến thể dương -> BasePrice. Không gọi `Retail` với mặc định chấp nhận 0 rồi coi đó là override giống POS. Hệ số thùng/lốc chỉ đổi base quantity, không nhân lại đơn giá đã lưu.
- Khách vãng lai thuộc giá lẻ; không lấy hạng giá sỉ/điểm/voucher của một khách cũ chỉ nhờ trùng số điện thoại. Không có sửa tay giá/chiết khấu của thu ngân trên màn hình khách.
- Tái sử dụng bộ máy promotion cho các chương trình tự động phù hợp khách lẻ, cùng cách ưu tiên, loại trừ và làm tròn hiện tại. Tách adapter quote khỏi thao tác lưu giỏ POS. Snapshot ID/phiên bản chương trình, dòng tặng và phân bổ giảm giá. Không viết công thức giảm giá web riêng.
- Chỉ áp chương trình có toàn bộ sản phẩm/đơn vị cần bán hoặc tặng hợp lệ cho kênh web; không tự thêm SKU chưa được cửa hàng cho phép. Nếu chương trình không áp dụng, catalog/quote không quảng cáo quà hoặc giá đã giảm. Tổng đơn 0 chưa được hỗ trợ bản đầu; dòng quà 0 trong đơn dương được giữ theo engine hiện tại. Không triển khai chương trình loyalty/AI mới trong WEB00.
- Checkout luôn reprice/revalidate ở máy chủ. Giá/phí/ưu đãi thay đổi -> gửi quote mới và yêu cầu khách xác nhận; không thu số cũ rồi âm thầm thay tổng.

### 6.2 Công thức phí ship đã khóa

`W = Σ(số lượng đơn vị bán × kg của chính đơn vị bán)`.

Nếu bật theo kg: `bước thêm = Ceiling(Max(0, W - kg mức đầu) / kg mỗi bước)`; `phí = phí mức đầu + bước thêm × phí mỗi bước`. Mức miễn ship áp dụng trên tiền hàng ròng sau giảm giá, không tính chính phí ship vào ngưỡng. Đạt đúng ngưỡng thì miễn. Nếu tắt kg: lấy phí cố định của vùng, không cần dữ liệu kg để đặt.

Ví dụ bảng phí minh họa **chỉ dùng test**: mức đầu 1kg/20.000, thêm mỗi 0,5kg/5.000. 1kg -> 20.000; 1,0001kg -> 25.000; 1,5kg -> 25.000; 1,5001kg -> 30.000. Không tự cộng 1kg cho mỗi dòng hoặc nhân kg thùng với hệ số thùng lần hai.

Kg thiếu/0/âm bị chặn khi theo kg; không coi là 0. Hàng cồng kềnh phải có vùng/phương thức và phụ phí đã cấu hình trước; bản đầu không có một phụ phí ngầm. Giới hạn kích thước/phương thức chưa được cấu hình thì báo không giao tự động, không nhận giá ước đoán.

Máy chủ chuẩn hóa địa chỉ, chọn vùng cụ thể ưu tiên hơn vùng chung. Giao gần có đội nội bộ và vùng phục vụ thật; ngoài vùng tự chuyển luật toàn quốc hợp lệ. Không tin nhãn local/national hoặc phí do trình duyệt gửi. Chưa có tích hợp hãng ở bản đầu; cửa hàng nhập mã vận đơn và đối soát từ chứng cứ thật.

### 6.3 Bán âm và ngân sách tồn

- Một quyết định `AllowNegative` đáng tin dùng xuyên suốt quote/đặt/giữ/xuất của kênh web, theo cấu hình bán âm đã chốt cho cửa hàng. Tồn 0/âm vẫn được nhận đơn khi bật. Không sửa cờ kho toàn hệ thống để làm web chạy, không để reserve từ chối trong khi xuất POS cho âm.
- Giỏ chưa đặt không giữ hàng. Đặt đơn tăng `ReservedQty`, không tăng `OnHand`. Reserve sử dụng cùng khóa nguồn số dư và kiểm tra tổng giữ của POS, giao hàng và website.
- Giả sử OnHand=0, các đơn khác giữ 2, web đặt 3 khi bật âm: OnHand vẫn 0, tổng giữ 5. Hủy web -> tổng giữ còn 2. Xuất web -> OnHand=-3, giữ đơn khác vẫn 2. Chốt bán sau đó không trừ tiếp 3.
- Khi cấm âm, giữ mới yêu cầu `OnHand - AllHeld >= Requested`; khi xuất yêu cầu bảo toàn giữ của đơn khác. Khi bật âm, hệ thống ghi cảnh báo thiếu tồn/giá vốn tạm theo policy có sẵn, không tạo nhập kho giả.
- Typed posting xử lý xuất/nhận lại; lưu unique source key, quantity/cost slices. Giá vốn tạm và điều chỉnh sau nhận hàng kế thừa cơ chế hiện hành. Kiểm thử WEB06 phải chứng minh chốt tài chính không xuất/cost lần hai.
- QR hết hạn theo cấu hình (không lấy 15 phút trong mockup). Tra cứu provider trước khi hủy/nhả giữ; timeout/unknown giữ nguyên và review, có cảnh báo vận hành. Không tự gia hạn vô hạn mà không hiển thị đơn cần xử lý.

## 7 Màn hình và nhận diện

Mẫu hình thức: `C:/Users/wind/.codex/visualizations/2026/10/30/01a18b7b-d967-75a2-8e05-76415bdd3f40/gao-market-refined.html`.

| Màn hình | Bản đầu cần có | Mốc |
|---|---|---|
| Trang chủ | Hero, nhóm hàng chọn thủ công, banner giữa, sản phẩm hot, CTA rõ, điều hướng điện thoại | WEB03 |
| Danh mục/tìm kiếm | Tìm không dấu, bộ lọc, sort, trang kết quả, loading/rỗng/lỗi | WEB03 |
| Chi tiết sản phẩm | Ảnh, biến thể/đơn vị, giá thật, mô tả, chính sách giao, số lượng; media 3D khi có | WEB03/08 |
| Giỏ | Sửa số lượng/đơn vị/xóa, giá tạm, cảnh báo hàng ẩn/thay đổi, tiếp tục mua | WEB03/04 |
| Checkout | Không đăng nhập; người nhận, địa chỉ, local/national theo server, phí/tổng, COD/QR, xác nhận đổi quote | WEB04 |
| QR/trả tiền | QR có số đơn, số tiền, tài khoản đúng; đang kiểm tra/thành công/hết hạn/Review; mở lại từ liên kết riêng | WEB05 |
| Tra cứu/chi tiết đơn | Liên kết riêng; timeline giao và tiền độc lập; hủy/yêu cầu hủy/tiến độ hoàn theo quyền | WEB04–07 |
| Admin cấu hình | Bật bán, thương hiệu, nguồn kho, phương thức, kg/vùng/phí/hạn, kiểm tra thiếu cấu hình | WEB01 |
| Admin hàng web | Chọn đăng/ẩn, variant/unit, nội dung/ảnh/kg, lọc trạng thái, bulk có kết quả | WEB02 |
| Admin đơn và giao | Queue, cảnh báo âm, soạn/bàn giao, bằng chứng giao/thu hồi; phiên bản/xung đột rõ | WEB06 |
| Admin tiền | Inbox Review, ghép receipt, COD collector/remittance/deduction, hoàn thực trả, Delivered chưa ghi sổ | WEB05–07 |

Đã có hướng người dùng chấp nhận tương đối (khoảng 8/10): xanh đậm, nền trắng, chữ đen, khối xanh chữ trắng, font Inter tiếng Việt, giá/nút dễ nhìn. Đây là baseline, chưa phải nghiệm thu giao diện cuối. Không sử dụng logo/tài sản nhận diện Starbucks làm thương hiệu cửa hàng.

Banner giữa từng bị phản hồi “Chọn vừa ý. Mua vừa túi.” khó đạt yêu cầu. Bản mới đổi thành **“Chọn món hợp ý. / Vừa túi tiền.”**, mô tả ngắn và nút “Xem gợi ý” có viền riêng, nền xanh/chữ trắng. Đã đưa vào mẫu nhưng **chưa có xác nhận cuối của chủ cửa hàng sau lần sửa này**. Hero giữ “Nhà xinh. Sống tiện.”. Tên “Gao Market” chỉ minh họa, nhập thương hiệu thật ở cấu hình.

Chuẩn triển khai: body khoảng 16px, contrast chữ thường ít nhất 4,5:1, tiêu đề lớn 3:1; nút chạm tối thiểu 44px; focus nhìn rõ; dùng được ở 360/390/430px và desktop, không tràn ngang. Animation phản hồi nút/giỏ, chuyển panel và skeleton không chặn thao tác; hỗ trợ reduced motion. Three.js lazy load theo vùng nhìn/ý định xem, có ảnh thay thế khi lỗi/WebGL không hỗ trợ. Đo hiệu năng trên máy điện thoại ở WEB08, không coi mẫu 3D primitive là mô hình sản phẩm thật đã hoàn thành.

Đường dẫn chức năng dự kiến: khách `/shop`, `/shop/products`, `/shop/cart`, `/shop/checkout`, `/shop/orders/...`; quản trị `/admin/storefront/settings`, `/admin/storefront/products`, `/admin/storefront/orders`, `/admin/storefront/payments`. Đây chưa phải route đã tồn tại. WEB01 trở đi khai báo quyền và đưa đường dẫn để người dùng tự tạo menu. **Không hardcode sidebar, seed/migration/script tạo menu hoặc tạo mục menu giả**, theo [AGENTS.md](../../AGENTS.md).

## 8 Ranh giới kiểm thử và cổng chuyển bước

Các file Domain kiểm chứng được ngay: [workflow](../../GaoApp.Domain/Storefront/StorefrontWorkflowPolicy.cs), [payment](../../GaoApp.Domain/Storefront/StorefrontPaymentPolicy.cs), [shipping](../../GaoApp.Domain/Storefront/StorefrontShippingPolicy.cs), [inventory](../../GaoApp.Domain/Storefront/StorefrontInventoryPolicy.cs), [financial](../../GaoApp.Domain/Storefront/StorefrontFinancialPolicy.cs).

Test WEB00 chạy riêng, không dùng startup Web, SQL, ngân hàng hoặc fixture dữ liệu tiệm. Một người thực hiện test độc lập với người viết policy; một lượt review độc lập đọc nguồn và hợp đồng sau test. Bằng chứng phải có nguồn hash, lệnh, SDK, số case thực chạy, lỗi/sửa/retest và giới hạn phạm vi. Không gọi đây là kiểm thử tích hợp sản xuất.

| Bước kế tiếp | Điều phải chứng minh ở bước đó |
|---|---|
| WEB01 | Migration sạch/nâng cấp, FK/unique/rowversion, mặc định đóng và chưa publish, giữ ràng buộc POS cũ, không tạo menu |
| WEB02/03 | Predicate publication thống nhất, giá theo unit đúng POS, font/contrast/mobile và trạng thái lỗi |
| WEB04 | Giá/promotion parity trên fixture, authoritative quote, guest token, idempotency transaction, giữ hàng chung và đồng thời với POS |
| WEB05 | Provider sandbox hoặc transport giả được phân biệt, callback auth/dedup cả hai kênh, ngoài ca, timeout/late receipt, dòng tiền thực nhận và chống mirror trùng |
| WEB06 | Nguồn giao web hợp lệ không giả ca, xuất một lần/giá vốn tạm, QR trả trước, COD net remit có chứng cứ, bridge ca thật và owner slices, báo cáo/invoice không trùng |
| WEB07 | Hủy/callback/dispatch/expiry races trong SQL, nhận trả vật lý đúng budget, refund không vượt và không tự chuyển tiền |
| WEB08/09 | Thiết bị/mobile/hiệu năng/accessibility, UAT, tải, vận hành/backup/rollback và cổng mở bán |

Cổng WEB00 đã đạt: hợp đồng đủ để triển khai, test thuần và review độc lập đạt, không còn lỗi chặn đã biết trong phạm vi, chủ cửa hàng đã chọn StoreServiceFee cho phí giao. Các thông số và cấu hình thực tế được triển khai/kiểm tra trong WEB01–WEB09 trước mở bán; website vẫn chưa mở bán. Trạng thái WEB01 xem [báo cáo triển khai](STOREFRONT-WEB01-20261030.md); không commit/push/deploy trong các bước này.
