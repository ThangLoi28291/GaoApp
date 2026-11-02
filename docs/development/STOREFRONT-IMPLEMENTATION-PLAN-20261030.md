# Kế hoạch triển khai website bán hàng GaoApp

Ngày lập: 30/10/2026. Trạng thái cập nhật 02/11/2026: WEB00–WEB07 đã hoàn thành; WEB08 PASS phần triển khai, WEB09 PASS phần chuẩn bị trước phát hành. Owner UAT và điều kiện thực tế còn mở; website chưa publish/mở bán. Xem [hợp đồng WEB00](STOREFRONT-WEB00-CONTRACT-20261030.md), [kết quả WEB00](STOREFRONT-WEB00-RESULT-20261030.md), [WEB01](STOREFRONT-WEB01-20261030.md), [WEB02](STOREFRONT-WEB02-20261031.md), [WEB03](STOREFRONT-WEB03-20261031.md), [WEB04](STOREFRONT-WEB04-20261031.md), [WEB05](STOREFRONT-WEB05-20261031.md), [WEB06](STOREFRONT-WEB06-20261031.md) và [WEB07](STOREFRONT-WEB07-20261101.md).

Mốc lịch sử ngày 01/11/2026: người dùng đồng ý xóa đúng 1.510 XML test WEB07 và chuyển WEB08; việc xóa đã được review đạt, giữ nguyên 1.272 XML context-only. WEB08 sau đó đã PASS phần triển khai. Theo yêu cầu tiếp theo ngày 02/11/2026, WEB09 đã hoàn tất code/test/review phần chuẩn bị, tạm chưa publish. Các hồ sơ và gate cũ được giữ đúng theo thời điểm; không thay thế nghiệm thu thực tế.

Mục tiêu là bổ sung kênh bán hàng công khai cho một cửa hàng gia dụng, tạp hóa và hàng hot. Khách xem hàng, đặt mua không cần tài khoản, thanh toán COD hoặc QR và theo dõi đơn trên điện thoại. Website dùng giá bán lẻ, kho và nền tảng thanh toán của GaoApp.

Triển khai theo WEB00 đến WEB09. Mỗi bước có đầu ra và điều kiện hoàn thành. Bước đầu tiên phải khóa cách nối đơn website với ca POS, QR, giao hàng và sổ tiền trước khi xây dựng checkout. Không coi bản phác thảo chạy bằng dữ liệu mô phỏng là chức năng đã được triển khai.

## 1 Phạm vi đã thống nhất

| Nội dung | Yêu cầu thực hiện |
|---|---|
| Cửa hàng | Một cửa hàng; dữ liệu tiếp tục nằm trong phạm vi cửa hàng của GaoApp |
| Hàng hóa | Gia dụng, tạp hóa, hàng đang hot |
| Đăng bán | Cửa hàng tự chọn sản phẩm; bật/tắt hiển thị từng sản phẩm; sản phẩm mới mặc định chưa đăng website |
| Giá | Cùng nguồn và quy tắc giá bán lẻ đang dùng; kiểm tra theo biến thể và đơn vị bán |
| Khách mua | Đặt hàng và thanh toán không cần tài khoản |
| Tồn kho | Dùng chung kho hiện tại; hỗ trợ bán âm như luồng bán của GaoApp |
| Tiếp nhận | Tự tiếp nhận đơn hợp lệ; không thêm bước nhân viên duyệt mọi đơn |
| COD | Áp dụng cho giao gần và toàn quốc |
| QR | Tái sử dụng nền tảng tự nhận biết tiền về của GaoApp; tự xác nhận thanh toán đúng đơn |
| Giao gần | Đội giao hàng cửa hàng; khu vực và phí giao cấu hình được |
| Giao toàn quốc | Nhận địa chỉ toàn quốc; phí được tính và hiển thị trước khi đặt |
| Phí ship | Có chế độ tự tính theo tổng kg; chế độ tắt dùng bảng phí cố định theo khu vực |
| Ghi nhận phí ship | Cửa hàng thu phí giao hàng cho cả giao gần và toàn quốc; chi phí trả shipper/hãng ghi riêng (`StoreServiceFee`), đã được chủ cửa hàng chọn |
| Hủy trước chuẩn bị | Khách tự hủy khi chờ thanh toán hoặc chờ chuẩn bị |
| Hủy từ lúc chuẩn bị | Khách gửi yêu cầu; cửa hàng xử lý theo trạng thái chuẩn bị và giao thực tế |
| Đơn đã trả tiền bị hủy | Theo dõi riêng chờ hoàn tiền và đã hoàn tiền |
| Giao diện | Ưu tiên điện thoại; nền trắng chữ đen, khối xanh chữ trắng theo hướng đã đối chiếu Starbucks |
| Font | Inter hỗ trợ tiếng Việt; nội dung chính khoảng 16px; giá và nút mua rõ; tiêu đề xuống dòng cân đối |
| Chuyển động | Animation phản hồi thao tác, sản phẩm 3D và hiệu ứng có kiểm soát; hỗ trợ giảm chuyển động |
| Điều hướng khách | Trang chủ, Danh mục, Giỏ hàng, Tra cứu đơn |

Bản tham chiếu mới nhất trong cuộc trò chuyện là `gao-market-refined.html`, nằm tại `C:/Users/wind/.codex/visualizations/2026/10/30/01a18b7b-d967-75a2-8e05-76415bdd3f40/`. Bản này dùng dữ liệu giả và đã tinh chỉnh banner giữa trang. Khi đưa vào dự án, dùng làm mẫu hình thức và thao tác; xây giao diện sản phẩm bằng dữ liệu và thành phần thực tế của GaoApp. Tên “Gao Market” là tên minh họa, tên và nhận diện cửa hàng thật sẽ cấu hình riêng.

Các ý tưởng có trong bản mẫu như lưu món, so sánh, mua lại và gợi ý theo nhu cầu được xếp trong phần mở rộng tại mục 8. Phần AI thật, AR, kết nối hãng vận chuyển và tự động chuyển tiền hoàn cần quyết định phạm vi riêng.

## 2 Những phần hiện có và điểm cần nối

| Nền tảng hiện tại | Điểm cần xử lý cho website | Nguồn trong dự án |
|---|---|---|
| Sản phẩm có trạng thái hoạt động và được phép bán | Thêm cấu hình đăng website độc lập; tắt website không thay cờ bán tại quầy | [Product.cs](../../GaoApp.Domain/Entities/Product.cs) |
| Giá bán theo sản phẩm, biến thể và đơn vị quy đổi | Dùng chính sách giá lẻ dùng cho POS; không tự nhân giá với số lượng trong thùng | [ProductSalePricePolicy.cs](../../GaoApp.Application/Services/Products/ProductSalePricePolicy.cs), [ProductUnitConversion.cs](../../GaoApp.Domain/Entities/ProductUnitConversion.cs) |
| `Order` bắt buộc có `POSShiftId` | Đơn website phải được tiếp nhận ngoài giờ và ngoài phiên thu ngân; cần ranh giới đơn online và chứng từ POS | [Order.cs](../../GaoApp.Domain/Entities/Order.cs) |
| QR ACB tạo và xử lý theo đúng ca/quầy | Tách phần thanh toán dùng chung khỏi điều kiện thao tác thu ngân; giữ các kiểm tra tại POS | [AcbPaymentService.cs](../../GaoApp.Web/Services/Acb/AcbPaymentService.cs) |
| Hoàn tất QR của POS có thể gọi `pos.FinalizeAsync` | Nhận tiền online phải cập nhật thanh toán theo hợp đồng đã khóa; không gọi thẳng luồng này để kết thúc bán và giao hàng | [AcbPaymentService.Installments.cs](../../GaoApp.Web/Services/Acb/AcbPaymentService.Installments.cs) |
| POS cho phép xuất bán âm; giữ hàng có kiểm tra riêng theo kho | Đồng nhất chính sách đặt đơn, giữ hàng và xuất kho cho kênh website; giữ nguyên các lượng giữ hàng khác | [POSService.cs](../../GaoApp.Application/Services/Orders/POSService.cs), [InventoryReservationService.cs](../../GaoApp.Application/Services/Inventory/InventoryReservationService.cs) |
| Giao hàng có chứng từ, người soạn, bàn giao, kết quả và đối soát | Luồng tạo từ POS hiện cần người dùng, quầy và ca; phải bổ sung nguồn website qua hợp đồng rõ ràng | [DeliveryPosService.cs](../../GaoApp.Web/Services/Delivery/DeliveryPosService.cs), [DeliveryPersistence.cs](../../GaoApp.Domain/Delivery/DeliveryPersistence.cs), [DeliveryContracts.cs](../../GaoApp.Domain/Delivery/DeliveryContracts.cs) |
| Tenant được xác định theo host/subdomain | Route công khai phải dùng đúng cửa hàng đã xác định trên máy chủ; tên miền bán hàng cần được cấu hình hợp lệ | [TenantResolutionMiddleware.cs](../../GaoApp.Web/Middlewares/TenantResolutionMiddleware.cs) |
| Dự án có Domain, Application, Infrastructure và Web .NET 10 | Giữ các lớp dùng chung; thêm project host khách hàng riêng trong cùng solution | [GaoApp.Web.csproj](../../GaoApp.Web/GaoApp.Web.csproj), [GaoApp.Storefront.Web.csproj](../../GaoApp.Storefront.Web/GaoApp.Storefront.Web.csproj) |
| Có migration catalog và schema manifest | Bổ sung schema theo cơ chế hiện hành, có kiểm tra nâng cấp và dữ liệu cũ | [EfCoreDatabaseMigrationCatalog.cs](../../GaoApp.Infrastructure/Data/Migrations/EfCoreDatabaseMigrationCatalog.cs), [DatabaseSchemaManifest.cs](../../GaoApp.Infrastructure/Data/Migrations/DatabaseSchemaManifest.cs) |

### Định hướng kiến trúc cho WEB00

- Website khách hàng nằm trong project riêng `GaoApp.Storefront.Web` cùng solution, chạy và triển khai riêng theo yêu cầu bổ sung của chủ cửa hàng trong WEB01. Cấu hình/quản trị nằm trong `GaoApp.Web`; hai host dùng chung Domain/Application/Infrastructure và dữ liệu cửa hàng. Project khách hàng không tham chiếu `GaoApp.Web` hoặc nạp controller Admin/POS.
- Vòng đời đơn online độc lập với giỏ đang mở ở máy thu ngân. Phương án ưu tiên là một aggregate đơn website, cùng các liên kết ổn định tới thanh toán, giao hàng và chứng từ bán hiện có. Tên bảng, quan hệ và thời điểm tạo chứng từ được khóa ở WEB00.
- Đơn online, thanh toán và giao hàng có trạng thái riêng. Báo cáo bán, doanh thu, thuế, giá vốn, tiền mặt và tiền ngân hàng tiếp tục đi qua hợp đồng nghiệp vụ của GaoApp.
- Không tạo ca/quầy giả, tắt điều kiện bảo vệ của POS hoặc dùng ngữ cảnh nhân viên giả để cho khách mua công khai.
- Tái sử dụng phần xử lý giao dịch ngân hàng, chống ghi nhận trùng và đối soát. Giao diện khách không truy cập API quản trị ngân hàng.
- Dùng Razor và thành phần CSS/JavaScript theo cấu trúc dự án. Three.js tải khi cần xem 3D. Không bê nguyên fragment mô phỏng cùng các thư viện nhúng vào giao diện sản xuất.

## 3 Luồng và quy tắc bắt buộc

### Đặt hàng

1. Khách xem sản phẩm đang đăng bán, chọn đúng biến thể, đơn vị và số lượng.
2. Giỏ hàng lưu lựa chọn; thêm giỏ chưa giữ hàng.
3. Máy chủ báo giá theo giá bán lẻ, địa chỉ nhận và cấu hình ship hiện hành.
4. Khách kiểm tra tổng tiền và chọn COD hoặc QR.
5. Khi đặt, máy chủ kiểm tra lại dữ liệu và tạo đơn cùng lượng giữ hàng trong một giao dịch nhất quán. Một lần đặt thử lại chỉ tạo một đơn.
6. COD vào chờ chuẩn bị. QR vào chờ thanh toán, tiền về hợp lệ thì vào chờ chuẩn bị.
7. Khách nhận mã đơn và liên kết riêng để xem đơn, mở lại QR và thực hiện hành động được phép.

Nếu giá hoặc phí thay đổi giữa báo giá và xác nhận, khách phải thấy số tiền mới trước khi đặt. Đơn đã tạo lưu bản chụp giá, quy cách, trọng lượng, cấu hình phí được áp dụng và tổng tiền; thay đổi cấu hình chỉ áp dụng cho báo giá và đơn mới.

### Kho và bán âm

- Đặt đơn, giữ hàng và xuất kho dùng cùng chính sách bán âm cho kênh website. Khi cho bán âm, tồn 0 hoặc âm không phải lý do tự từ chối đơn hợp lệ.
- Lượng giữ theo dõi nhu cầu của đơn; không cộng lượng giữ vào tồn vật lý. Ghi xuất vật lý theo bước đã khóa trong hợp đồng giao hàng.
- Nhân viên thấy đơn có hàng thiếu tồn để chuẩn bị. Website hiển thị điều kiện và thời gian giao phù hợp với cấu hình, không suy diễn tồn âm thành cam kết có hàng giao ngay.
- Hủy trước xuất kho giải phóng lượng giữ. Hàng đã xuất chỉ nhập lại khi có nghiệp vụ nhận lại hàng thực tế.
- Tắt đăng bán chặn đơn mới, kể cả khách đã để hàng trong giỏ; đơn cũ tiếp tục được xử lý theo dữ liệu đã lưu.

### Phí giao hàng

- Tổng kg bằng tổng trọng lượng từng đơn vị bán nhân số lượng. Đơn vị kg này phục vụ vận chuyển, tách khỏi đơn vị tồn kho và giá bán.
- Chế độ theo kg dùng mức đầu, bước tăng thêm và cách làm tròn được cấu hình cho khu vực. Chế độ cố định lấy mức phí của khu vực.
- Máy chủ xác định phương thức giao hợp lệ theo địa chỉ và vùng phục vụ; khách không thể chọn nhãn “giao gần” để áp phí rẻ cho địa chỉ ngoài vùng.
- Khi tự tính theo kg, đơn vị được đăng bán phải có trọng lượng hợp lệ. Không tự thay trọng lượng thiếu bằng 0.
- Mức miễn ship, phụ phí cồng kềnh và các con số minh họa trong bản mẫu phải được chủ cửa hàng cấu hình trước khi mở bán.

### Thanh toán và đối soát

- QR được gắn với đúng đơn, tài khoản nhận và số tiền phải thu. Website hoạt động kể cả khi khách đóng trang và quầy không có ca đang mở.
- Chỉ ghi đã thanh toán từ bằng chứng nhận tiền đã xác thực. Khách bấm thông báo đã chuyển tiền không tự đổi trạng thái tài chính.
- Callback trùng, đến lại sau lỗi hoặc tác vụ chạy lại không tạo thêm khoản thu.
- Tiền về sau khi đơn đã hủy được ghi nhận vào đối soát, có yêu cầu hoàn khi thích hợp; không tự mở lại đơn đã hủy.
- COD phải phân biệt khách đã trả người giao với tiền đã được đối soát về cửa hàng, đặc biệt khi giao qua đơn vị vận chuyển ngoài.
- Thời điểm ghi nhận doanh thu, chứng từ bán, hóa đơn, tiền vào ca và phần phí ship phải được khóa ở WEB00; không đồng nhất thao tác nhận tiền với hoàn tất giao hàng.

### Hủy và hoàn tiền

| Trạng thái | Hành động của khách | Xử lý hệ thống |
|---|---|---|
| Chờ thanh toán | Tự hủy | Hủy đơn, xử lý QR đang chờ và lượng giữ theo trạng thái nhận tiền thực tế |
| Chờ chuẩn bị | Tự hủy | Hủy đơn; giải phóng lượng giữ; đã trả tiền thì theo dõi hoàn tiền |
| Đang chuẩn bị | Gửi yêu cầu hủy | Nhân viên duyệt theo tiến độ; xử lý xung đột với thao tác chuẩn bị |
| Đang giao | Gửi yêu cầu hủy | Cửa hàng xử lý theo việc giao/thu hồi/nhận lại hàng thực tế |
| Đã hủy, đã nhận tiền | Xem tiến độ hoàn | Chờ hoàn tiền hoặc đã hoàn tiền có bằng chứng và người xác nhận |

Tác vụ hết hạn giữ hàng QR phải đối soát nhận tiền trước khi giải phóng. Nếu ngân hàng chưa xác định được kết quả, chuyển trạng thái chờ kiểm tra; không kết luận chưa trả tiền chỉ vì mất phản hồi. Đặt hàng, bắt đầu chuẩn bị, hủy, callback và hết hạn phải có kiểm tra phiên bản để một đơn không nhận các kết quả mâu thuẫn.

## 4 Thứ tự thực hiện

| Mã | Giai đoạn | Phụ thuộc | Đầu ra để nghiệm thu | Trạng thái |
|---|---|---|---|---|
| WEB00 | Khóa yêu cầu và hợp đồng tích hợp | Kế hoạch này | Kiến trúc, sơ đồ trạng thái, bảng ánh xạ tiền/kho/giao hàng | Hoàn thành; test và review độc lập đạt; phí ship đã chốt StoreServiceFee |
| WEB01 | Dữ liệu nền và cấu hình cửa hàng | WEB00 | Schema, quyền, cấu hình đăng bán và giao hàng; host khách hàng riêng | Hoàn thành; 178/178 test và review độc lập đạt |
| WEB02 | Quản lý hàng đăng website | WEB01 | Chọn hàng, ảnh, nội dung, biến thể, đơn vị và trọng lượng | Hoàn thành; test 342/342 và review độc lập đạt |
| WEB03 | Giao diện xem và chọn hàng | WEB02 | Trang chủ, danh mục, chi tiết, tìm kiếm, giỏ trên dữ liệu thật | Hoàn thành; 359/359 test và review độc lập đạt |
| WEB04 | Báo giá và đặt đơn khách vãng lai | WEB00 đến WEB03 | COD, phí ship, giữ hàng, bán âm, tự tiếp nhận và tra cứu | Hoàn thành; 640 ca distinct và review độc lập đạt |
| WEB05 | QR tự xác nhận thanh toán | WEB00, WEB01, WEB04 | QR và ghi nhận nhận tiền cho đơn online ngoài ca POS | Hoàn thành; 1.330 ID đạt theo phiên bản, review/126 identity/cleanup PASS |
| WEB06 | Chuẩn bị và giao hàng | WEB00, WEB04, WEB05 | Xử lý đơn, xuất giao, kết quả, đối soát COD và hàng trả lại | PASS; đã tiếp tục WEB07 theo yêu cầu mới |
| WEB07 | Hủy đơn và theo dõi hoàn tiền | WEB04 đến WEB06 | Hủy theo trạng thái, yêu cầu duyệt, tiền về muộn, hoàn tiền | PASS; dọn đúng 1.510 XML được đồng ý, giữ 1.272 XML context-only; review sau xóa đạt |
| WEB08 | Hoàn thiện animation và 3D | WEB03 đến WEB07 | Trải nghiệm hoàn chỉnh, ảnh/3D thật, hiệu năng và khả năng truy cập | PASS phần triển khai; hàng/model thật, thiết bị và owner UAT còn mở |
| WEB09 | Nghiệm thu và phát hành | WEB00 đến WEB08 | Hồ sơ kiểm thử, môi trường thử, hướng dẫn vận hành và kế hoạch mở bán | PASS phần chuẩn bị trước publish; điều kiện nghiệm thu/phát hành thực tế giữ lại |

Giao diện cơ bản được làm ở WEB03 và hoàn thiện cùng từng luồng nghiệp vụ. WEB08 tập trung lớp trải nghiệm cuối, không để toàn bộ phần nhìn đến cuối dự án. Chỉ mở bán công khai khi các bước bắt buộc đã đạt tiêu chí nghiệm thu.

### WEB00 Khóa yêu cầu và hợp đồng tích hợp

- [x] W00T01 Lập bảng chuyển trạng thái đơn online, thanh toán, giao hàng và hoàn tiền; ánh xạ với trạng thái hiện có của GaoApp.
- [x] W00T02 Chọn mô hình đơn online, thời điểm nối chứng từ POS và giao hàng, xử lý hoạt động ngoài ca; ghi thành quyết định kiến trúc có phương án được chọn và lý do.
- [x] W00T03 Khóa cách ghi nhận tiền hàng, phí ship, QR trả trước, COD nội bộ/COD vận chuyển, doanh thu, hóa đơn và giá vốn. Đối chiếu với luồng giao hàng và báo cáo hiện tại.
- [x] W00T04 Khóa chính sách giữ hàng khi bán âm, hết hạn QR, giá/ưu đãi bán lẻ áp dụng trên web và điều kiện hàng được đăng bán.
- [x] W00T05 Chốt bộ màn hình và nhận diện dựa trên bản phác thảo; ghi rõ phần đã duyệt và phần cần chủ cửa hàng góp ý.

W00T03 đã chốt theo trả lời của chủ cửa hàng “Làm theo cách 1”: `StoreServiceFee` cho giao gần và toàn quốc; phí khách trả thuộc dịch vụ giao của cửa hàng, chi phí trả shipper/hãng theo dõi riêng. WEB00 và WEB01 đã hoàn thành. Theo yêu cầu bổ sung trong WEB01, host khách hàng được tách thành project riêng. Bản banner giữa đã sửa nhưng chưa nhận xác nhận cuối; được ghi riêng trong hợp đồng để tiếp tục hoàn thiện UI, không coi là giao diện cuối đã nghiệm thu.

**Hoàn thành khi:** có hợp đồng tích hợp đủ để viết các bước sau, ví dụ tiền và tồn cho COD/QR/hủy khớp nhau, không còn phương án dựa vào ca/quầy giả. Chỉ đưa các quyết định kinh doanh còn ảnh hưởng số tiền hoặc vận hành cho chủ cửa hàng chốt; các lựa chọn kỹ thuật thông thường được xử lý trong bước này.

### WEB01 Dữ liệu nền và cấu hình cửa hàng

- [x] W01T01 Bổ sung cấu hình website: bật mở bán, tên/nhận diện, nguồn kho hiện tại, phương thức COD/QR, chính sách bán âm và thời gian giữ đơn chờ QR; lưu phí giao theo `StoreServiceFee` đã chốt cho cả giao gần/toàn quốc, chi phí giao ghi riêng.
- [x] W01T02 Bổ sung dữ liệu xuất bản sản phẩm, nội dung trưng bày, trọng lượng theo biến thể/đơn vị bán và thông tin media. Giữ tham chiếu tới sản phẩm gốc.
- [x] W01T03 Bổ sung vùng giao và bảng phí; thiết kế lưu đơn/bản chụp báo giá, liên kết thanh toán, giao hàng và yêu cầu hoàn theo quyết định WEB00.
- [x] W01T04 Thêm migration, ràng buộc, index, quyền truy cập và kiểm tra phạm vi cửa hàng theo cơ chế hiện hành.

Yêu cầu bổ sung đã nhận trong WEB01: tạo project `GaoApp.Storefront.Web` để khách truy cập, triển khai độc lập với Web quản trị. WEB01 tạo host nền đang đóng và health endpoint; catalog/checkout vẫn theo WEB03–WEB05.

**Hoàn thành khi:** database mới và database nâng cấp đều dùng được; dữ liệu POS cũ vẫn hợp lệ; website mặc định chưa mở và sản phẩm chưa tự đăng bán; không chèn dữ liệu menu.

### WEB02 Quản lý hàng đăng website

- [x] W02T01 Làm danh sách chọn hàng, tìm/lọc trạng thái đăng, bật/tắt từng sản phẩm và thao tác nhiều sản phẩm có kết quả rõ ràng.
- [x] W02T02 Làm phần thông tin website: tên hiển thị nếu cần, mô tả, ảnh, nội dung, nhóm trưng bày, thứ tự, biến thể và đơn vị được bán.
- [x] W02T03 Nhập/kiểm tra trọng lượng đúng đơn vị; khi bật ship theo kg phải có dữ liệu đủ cho các lựa chọn đang đăng bán.
- [x] W02T04 Đọc giá theo cùng quy tắc bán lẻ POS; trạng thái bán toàn hệ thống, biến thể và đơn vị vẫn được kiểm tra.

Kết quả và giới hạn của WEB02: [báo cáo 31/10](STOREFRONT-WEB02-20261031.md).

**Hoàn thành khi:** cửa hàng tự điều khiển hàng đăng web; tắt đăng có hiệu lực ở trang chủ, tìm kiếm, đường dẫn trực tiếp và xác nhận đặt hàng; giá theo đơn vị khớp POS; đơn cũ giữ được dữ liệu đã chốt.

### WEB03 Giao diện xem và chọn hàng

- [x] W03T01 Làm layout khách hàng, bốn đích điều hướng và bộ thành phần typography/màu/nút/biểu mẫu riêng cho website.
- [x] W03T02 Làm trang chủ, nhóm hàng hot do cửa hàng chọn, danh mục, tìm kiếm bỏ dấu, lọc/sắp xếp và phân trang.
- [x] W03T03 Làm chi tiết sản phẩm, ảnh, lựa chọn biến thể/đơn vị, số lượng và thêm giỏ/mua ngay.
- [x] W03T04 Lưu giỏ trên thiết bị; quay lại vẫn còn lựa chọn; kiểm tra lại giá và trạng thái sản phẩm khi bắt đầu đặt hàng.

Kết quả và giới hạn của WEB03: [báo cáo 31/10](STOREFRONT-WEB03-20261031.md).

**Hoàn thành khi:** xem/chọn hàng thật trên màn 320, 360, 390, 430px và desktop không chồng chữ hoặc tràn ngang; tiêu đề và giá dễ đọc; tồn âm không tự làm sản phẩm bị khóa mua khi chính sách cho phép.

### WEB04 Báo giá và đặt đơn khách vãng lai

- [x] W04T01 Tạo dịch vụ báo giá trên máy chủ: giá lẻ, số lượng/quy cách, địa chỉ, phương thức giao, tổng kg, phí và tổng tiền.
- [x] W04T02 Làm form nhận hàng một luồng, kiểm tra dữ liệu và hiển thị lỗi tại trường; bắt đầu bằng đặt COD hoàn chỉnh.
- [x] W04T03 Tạo đơn và lượng giữ theo một lần đặt duy nhất; chống tạo trùng khi nhấn lại hoặc gửi lại sau mất mạng; áp dụng bán âm xuyên suốt.
- [x] W04T04 Cấp mã đơn và liên kết riêng, trang trạng thái và thông báo đơn mới trong GaoApp; giữ thông tin khách vãng lai theo phạm vi đơn.

Kết quả và giới hạn của WEB04: [báo cáo 31/10](STOREFRONT-WEB04-20261031.md). Liên kết riêng dùng fragment `/shop/orders#<token>` và POST token trong body để tránh token xuất hiện trong URL request/log.

**Hoàn thành khi:** khách mua COD không đăng ký; phí đúng theo cấu hình; đơn tự vào chờ chuẩn bị; gửi lại cùng lệnh nhận lại cùng đơn; thao tác đồng thời với POS giữ đúng các số liệu kho.

### WEB05 QR tự xác nhận thanh toán

- [x] W05T01 Tách/tái sử dụng phần QR và xử lý ngân hàng theo hợp đồng WEB00; bổ sung liên kết đơn online và tổng phải thu gồm các khoản đã khóa.
- [x] W05T02 Làm màn QR, tải ảnh, sao chép nội dung, mở lại QR và cập nhật trạng thái; dùng cùng yêu cầu thanh toán khi mở lại hợp lệ.
- [x] W05T03 Xử lý callback/đối soát có xác thực, tác vụ chạy trên máy chủ, thử lại và kết quả chưa rõ; không phụ thuộc trang khách đang mở.
- [x] W05T04 Kiểm tra tiền đúng, tiền lệch/thiếu/thừa, callback trùng, nhận tiền ngoài ca và tiền về sau hủy.

Kết quả và giới hạn của WEB05: [báo cáo 31/10](STOREFRONT-WEB05-20261031.md). Các ca có transport kiểm soát không phải UAT ngân hàng thật; production tiếp tục đóng.

**Hoàn thành khi:** tiền về đúng được ghi nhận một lần, đơn chuyển sang chờ chuẩn bị; không tự coi đã giao hàng; các thao tác QR tại POS hiện có giữ đúng hành vi và kiểm tra quyền.

### WEB06 Chuẩn bị và giao hàng

- [x] W06T01 Làm danh sách đơn website theo trạng thái, phương thức giao/thanh toán và cờ thiếu tồn; có lịch sử ai xử lý.
- [x] W06T02 Nối luồng soạn, bàn giao, xuất giao và nhận lại hàng bằng hợp đồng nghiệp vụ đã chốt; không ghi trực tiếp số dư kho từ controller website.
- [x] W06T03 Giao gần dùng đội cửa hàng; giao toàn quốc lưu đơn vị vận chuyển, mã vận đơn và kết quả. Đợt đầu cho phép cập nhật bởi người có quyền, không phụ thuộc bắt buộc API hãng vận chuyển.
- [x] W06T04 Tách kết quả giao, tiền đã thu khách, tiền đã bàn giao/đối soát về cửa hàng; xử lý giao thất bại và hàng quay lại.

Kết quả WEB06: [báo cáo 31/10](STOREFRONT-WEB06-20261031.md). Code/test/review độc lập đã đạt; 1.706 ID distinct PASS theo phiên bản, gồm 874 ca chạy trên bản cuối WEB06. Đã tạm ngưng tại WEB06; ngày 01/11/2026 chủ cửa hàng yêu cầu tiếp tục riêng WEB07. Các kết quả WEB06 không tự được coi là test đã chạy trên bản WEB07.

**Hoàn thành khi:** một đơn đi được từ chờ chuẩn bị đến hoàn tất hoặc nhận lại hàng; xuất/nhập và khoản thu không lặp khi thử lại; báo cáo tiền/kho truy ngược được nguồn đơn. Giao thiếu, đổi hàng hoặc tách kiện chỉ mở khi đã có hợp đồng và ca kiểm thử tương ứng.

### WEB07 Hủy đơn và theo dõi hoàn tiền

- [x] W07T01 Làm tự hủy trước chuẩn bị và yêu cầu duyệt từ khi đang chuẩn bị/giao; kiểm tra quyền và phiên bản ở máy chủ.
- [x] W07T02 Gỡ lượng giữ hoặc nhận lại hàng theo thực tế; xử lý đồng thời với nhân viên bắt đầu chuẩn bị/xuất giao.
- [x] W07T03 Theo dõi yêu cầu hoàn tiền: số tiền, lý do, người xử lý, bằng chứng thực hiện và trạng thái; khách xem được tiến độ phù hợp.
- [x] W07T04 Khép kín luồng tiền về muộn, hủy khi ngân hàng chưa rõ kết quả và hết hạn giữ QR.

**Hoàn thành khi:** khách tự hủy đúng phạm vi đã thống nhất; đã trả tiền thì có khoản cần hoàn rõ ràng; không ghi đã hoàn trước khi có xác nhận thực hiện; không tự chuyển tiền ra tài khoản khách.

Kết quả WEB07: APP8/Test12, 1.921/1.921 ID thực PASS trên S12; WEB00 riêng 378 lượt kiểm tra PASS. Review độc lập source, test, browser và union đã đạt. Sau đồng ý trực tiếp, đã dọn đúng 1.510 XML test, giữ nguyên 1.272 XML context-only; review độc lập sau xóa và 9.568 kiểm tra source/binary/runtime đều đạt. Gate WEB07 PASS với phạm vi giữ lại được ghi rõ. Chưa mở bán, chưa áp migration database cửa hàng. [Báo cáo WEB07](STOREFRONT-WEB07-20261101.md).

### WEB08 Hoàn thiện animation và 3D

- [x] W08T01 Hoàn thiện banner giữa trang, phân cấp tiêu đề, trạng thái tải/rỗng/lỗi, chuyển màn và phản hồi thêm giỏ/đặt đơn theo bản tham chiếu.
- [x] W08T02 Tích hợp xem 3D cho sản phẩm có tài nguyên phù hợp, hỗ trợ xoay/đổi lựa chọn, tải khi cần và giới hạn tài nguyên trên điện thoại.
- [x] W08T03 Dùng ảnh sản phẩm khi không có model hoặc không chạy được WebGL; thao tác mua vẫn hoàn chỉnh. Model demo không tự được coi là model thật của hàng đang bán.
- [x] W08T04 Hoàn thiện ảnh theo kích thước, font được đóng gói cùng giấy phép phù hợp, điều khiển bằng bàn phím, nhãn truy cập và giảm chuyển động theo thiết bị.

**Hoàn thành khi:** 3D và animation không che chữ, gây nhảy bố cục hoặc cản mua; người dùng vẫn mua được khi tắt hiệu ứng; ảnh, mô tả và mô hình khớp mặt hàng thật được đưa lên web.

**Kết quả triển khai WEB08 (02/11/2026):** W08T01–04 đã code/test/review độc lập đạt trên APP9/Test5/S9: 1.999 test hiện tại PASS, WEB00 riêng 378 assertion PASS; nguồn, chữ 200%, hiệu năng lab và tài nguyên đã review PASS. Đây là PASS phần triển khai. Theo A01/oracle trước viết code, duyệt ảnh/mô tả/model khớp hàng thật và UAT/thiết bị thực trước phát hành còn mở; fixture không thay thế nghiệm thu đó. Tiêu chí hoàn thành thực tế ở trên được giữ nguyên. WEB09 đã bắt đầu theo yêu cầu tiếp theo, vẫn giữ điều kiện phát hành. Chi tiết: [báo cáo WEB08](STOREFRONT-WEB08-20261102.md).

### WEB09 Nghiệm thu và phát hành

- [x] W09T01 Chạy các ca chức năng, SQL thật, browser và hồi quy liên quan trong mục 5 trên môi trường thử riêng.
- [ ] W09T02 Chủ cửa hàng thử mua COD/QR, thay bảng phí, bật/tắt sản phẩm, soạn/giao và hủy/hoàn; ghi nhận kết quả và các lỗi còn lại.
- [ ] W09T03 Hoàn thiện tên miền, nhận diện, thông tin liên hệ, bảng phí, dữ liệu hàng, hướng dẫn vận hành và cảnh báo đơn cần xử lý.
- [ ] W09T04 Thử nâng cấp và khôi phục vận hành; mở bán sau nghiệm thu, theo dõi đơn/tiền/kho trong giai đoạn đầu.

**Hoàn thành khi:** các ca bắt buộc đạt; cấu hình và mặt hàng đã được cửa hàng kiểm tra; có hướng dẫn xử lý lỗi và cách dừng nhận đơn mới trong khi tiếp tục xử lý tiền/đơn cũ.

**Kết quả kỹ thuật WEB09 trước publish (02/11/2026):** APP4/Test7/S6 đã code/test/review độc lập đạt: union trực tiếp 2.066 ca thực PASS, WEB00 riêng 378 assertion PASS, không bỏ qua hoặc giữ outcome cũ. Có điều khiển bật/dừng, readiness, hướng dẫn, template UAT và diễn tập khôi phục COD/BANK bằng SQL riêng. W09T01 đạt. W09T02/03/04 vẫn chưa hoàn thành toàn bộ điều kiện gốc: owner UAT, cấu hình/hàng/media/thiết bị thật, domain/hosting/ngân hàng và mở bán còn giữ. Chưa publish hoặc migrate database cửa hàng. [Báo cáo WEB09](STOREFRONT-WEB09-20261102.md).

## 5 Kiểm thử và tiêu chí nghiệm thu

Các tiêu chí dưới đây là việc phải thực hiện khi triển khai, chưa phải báo cáo đã đạt trên backend.

| Nhóm | Ca kiểm thử bắt buộc |
|---|---|
| Đăng bán | Hàng chưa đăng, tắt đăng, biến thể/đơn vị ngừng bán; tắt khi đang có trong giỏ; đơn đã tạo không bị mất dữ liệu |
| Giá | Giá sản phẩm/biến thể/đơn vị khớp quy tắc POS bán lẻ; không nhân giá thùng lần nữa; thay giá giữa báo giá và đặt |
| Phí ship | Giao gần/toàn quốc, bật/tắt theo kg, đúng ngưỡng kg, làm tròn, số lượng nhiều, thiếu trọng lượng, địa chỉ ngoài vùng, sửa bảng phí |
| Khách vãng lai | Không cần đăng nhập; lỗi form giữ nội dung; mở lại giỏ; tra cứu bằng liên kết hợp lệ |
| Tạo đơn | Nhấn hai lần, gửi lại sau mất phản hồi, cùng mã lệnh nhưng nội dung khác, báo giá hết hiệu lực |
| Kho | POS và web bán đồng thời; tồn dương/0/âm; giữ, giải phóng, xuất và nhận lại đúng một lần; lượng giữ của đơn khác được bảo toàn |
| QR | Đúng đơn/số tiền/tài khoản, chữ ký hoặc bằng chứng hợp lệ, callback trùng/đảo thứ tự, retry, ngoài ca, đóng trang khách, ngân hàng lỗi |
| Vòng đời tiền | Tiền về đồng thời hủy/hết hạn; tiền về sau hủy; không tự mở lại đơn; nhận tiền không tự hoàn tất giao hàng |
| COD | Khách trả cho người giao, người giao/đơn vị vận chuyển bàn giao lại tiền; giao thành công nhưng chưa đối soát tiền |
| Hủy và hoàn | Tự hủy trước chuẩn bị; từ chối tự hủy sau mốc; duyệt yêu cầu; hủy sau xuất; nhận lại thực tế; xác nhận hoàn có bằng chứng |
| Phạm vi dữ liệu | Khách không thấy giá vốn, bí mật ngân hàng, dữ liệu khách khác hoặc cửa hàng khác; sửa `StoreId` từ client không chuyển phạm vi |
| Quyền quản trị | Quyền xem/xử lý đơn, đăng hàng, cấu hình và hoàn tiền được kiểm tra ở controller/service; ẩn menu không thay quyền |
| Tra cứu | Token đủ khó đoán; liên kết không hợp lệ/hết hiệu lực; giới hạn thử; mã đơn và số điện thoại không làm lộ lịch sử khách hoặc toàn bộ dữ liệu nhạy cảm |
| Hồi quy | POS tiền mặt/QR, ca/quầy, kho/giá vốn, giao hàng, hóa đơn và các báo cáo bị tác động vẫn đúng |
| Giao diện | 320/360/390/430px, tablet/desktop; bàn phím, tiêu đề dài, loading/rỗng/lỗi, mạng chậm, WebGL tắt, giảm chuyển động |

Đặt test nghiệp vụ trong `GaoApp.Tests` và test browser theo harness hiện có của `GaoApp.Tests.Browser`. Các ca tranh chấp đơn/tiền/kho và migration phải dùng fixture SQL Server riêng; không dùng database cửa hàng đang vận hành làm dữ liệu thử.

Mục tiêu chất lượng giao diện: chữ thường đạt tương phản tối thiểu 4,5:1; thao tác chính dễ chạm; nội dung chính khoảng 16px; không tràn ngang ở 320px. Mục tiêu hiệu năng ban đầu: LCP không quá 2,5 giây, INP không quá 200ms và CLS không quá 0,1 trong điều kiện đo được ghi rõ. Đây là mục tiêu nghiệm thu, không phải kết quả đo của bản mẫu. 3D tải sau phần nội dung mua hàng cần thiết.

## 6 Menu quyền và đường dẫn

Tuân thủ [AGENTS.md](../../AGENTS.md). Người dùng tự tạo và sắp xếp menu quản trị qua Quản lý menu. Sidebar chỉ hiển thị `AdminMenuItems` theo quyền, trạng thái và thứ tự đã cấu hình. Migration, seeder, script và mã chức năng mới không tự chèn menu hoặc mục giả vào màn ẩn/hiện menu.

Quyền chức năng được khai báo độc lập, kiểm tra ở controller/service và phạm vi cửa hàng. Khi hoàn thành từng màn quản trị, bàn giao URL thật cùng quyền cần có để cửa hàng tự tạo menu.

Các đường dẫn dưới đây là dự kiến để khóa ở WEB00, chưa phải chức năng đang hoạt động:

| Đích | Đường dẫn dự kiến |
|---|---|
| Website khách hàng | `/shop` |
| Danh sách sản phẩm | `/shop/products` |
| Giỏ hàng | `/shop/cart` |
| Đặt hàng | `/shop/checkout` |
| Theo dõi đơn bằng liên kết riêng | `/shop/orders#<token>`; đọc qua POST `/shop/orders/access` |
| Quản lý đăng sản phẩm | `/admin/storefront/products` |
| Xử lý đơn website | `/admin/storefront/orders` |
| Cấu hình website và giao hàng | `/admin/storefront/settings` |
| Theo dõi hoàn tiền | `/admin/storefront/refunds` |

Bốn đích điều hướng công khai đã thống nhất thuộc giao diện khách hàng. Chúng không được dùng để chèn chức năng cố định vào sidebar quản trị.

## 7 Dữ liệu và quyết định cần chuẩn bị

| Việc cần chốt | Bước xử lý | Người phụ trách |
|---|---|---|
| Mô hình đơn online và mốc nối chứng từ bán/giao hàng ngoài ca POS | WEB00 | Người triển khai; chủ cửa hàng chốt phần nghiệp vụ tiền và vận hành |
| Tiền hàng, phí ship, tiền QR trả trước, COD vận chuyển và doanh thu/hóa đơn | WEB00 | Chủ cửa hàng và người triển khai |
| Giá lẻ, ưu đãi đang có có áp dụng trên web hay không; hàng giá 0 | WEB00 | Chủ cửa hàng và người triển khai |
| Giữ hàng khi bán âm, thời hạn QR, đơn quá hạn/thiếu hàng | WEB00 | Chủ cửa hàng và người triển khai |
| Tên cửa hàng, logo, thông tin liên hệ và hostname hợp lệ | WEB01 đến WEB03, hoàn tất trước WEB09 | Chủ cửa hàng |
| Danh sách hàng đăng, lựa chọn đơn vị, ảnh, mô tả và trọng lượng | WEB02 | Chủ cửa hàng, có màn hình hỗ trợ kiểm tra |
| Vùng giao gần, bảng phí, bước kg, ngưỡng miễn phí và phụ phí nếu dùng | WEB01 và WEB04 | Chủ cửa hàng |
| Quy trình xác nhận đã hoàn tiền và người có quyền thực hiện | WEB07 | Chủ cửa hàng |
| Model 3D phù hợp với mặt hàng thật | WEB08 | Chủ cửa hàng và người triển khai |

Việc chọn hãng vận chuyển/API riêng, SMS, tên miền mới hoặc dịch vụ AI có chi phí được đưa thành lựa chọn cụ thể khi có nhu cầu. Kế hoạch này không tự đăng ký, mua hoặc tích hợp một nhà cung cấp chưa được chọn.

## 8 Phần mở rộng sau luồng bán hàng chính

| Chức năng | Hướng thực hiện |
|---|---|
| Lưu món và sản phẩm vừa xem | Lưu lựa chọn trên thiết bị; không yêu cầu tài khoản và không lộ lịch sử khách khác |
| Mua lại | Thêm hàng từ đơn được phép xem, kiểm tra lại trạng thái và giá hiện tại |
| So sánh | So sánh số lượng sản phẩm giới hạn theo thông tin, quy cách và giá thật |
| Gợi ý theo nhu cầu | Có thể bắt đầu bằng bộ lọc như góc bếp/ngân sách; đánh giá AI thật thành bước riêng |
| Nội dung hàng hot | Nhóm hàng, ảnh/video do cửa hàng chọn; không dựng số bán hoặc đánh giá giả |
| Kết nối hãng vận chuyển | Báo phí, tạo vận đơn, tracking và đối soát sau khi chọn nhà cung cấp và khóa hợp đồng |
| Tài khoản khách, điểm thưởng, voucher, cộng tác viên, AR | Chốt phạm vi và quyền sử dụng dữ liệu trước khi đưa vào đợt tiếp theo |

Animation và xem 3D đã yêu cầu nằm trong WEB08, không bị chuyển toàn bộ sang nhóm mở rộng này. Tự động chuyển tiền hoàn là nghiệp vụ riêng, ngoài phần theo dõi hoàn tiền của phiên bản đầu.

## 9 Cách thực hiện và cập nhật tiến độ

Chủ cửa hàng đã yêu cầu tự thực hiện tuần tự WEB02–WEB09, mỗi bước code/test/review độc lập và tự sửa đến khi đạt. Theo dõi cổng và bằng chứng tại [hồ sơ triển khai tuần tự](STOREFRONT-ROLLOUT-20261030.md).

1. Bắt đầu một mã WEB theo bảng phụ thuộc. Đọc lại source và trạng thái workspace tại thời điểm thực hiện.
2. Hoàn thành đầu ra, kiểm tra phù hợp và ghi rõ file thay đổi, cách thử, kết quả, phần còn lại của bước đó.
3. Cập nhật trạng thái và checklist ngay trong kế hoạch này. Chỉ đánh dấu hoàn thành khi đạt tiêu chí, không đánh dấu bằng việc chỉ có giao diện hoặc bản mô phỏng.
4. Tách thay đổi theo bước để dễ xem xét và khôi phục. Giữ nguyên công việc khác đang có trong workspace.
5. Chốt lại các thay đổi phạm vi có ảnh hưởng nghiệp vụ; lựa chọn kỹ thuật thông thường được thực hiện trong phạm vi đã thống nhất.
6. Khi phát hành, áp migration theo quy trình dự án; dừng nhận đơn mới nếu cần nhưng tiếp tục tiếp nhận callback, xử lý đơn và đối soát tiền đang tồn tại. Không rollback bằng cách xóa đơn, khoản thu hoặc giao dịch kho đã phát sinh.

**Bước bắt đầu: WEB00.** Đầu ra đầu tiên là hợp đồng đơn website, sơ đồ trạng thái và bảng ánh xạ tiền/kho/ca/giao hàng. Sau bước này mới chốt ước lượng ngày công và bắt đầu WEB01. Việc lập kế hoạch không tự mở bán, chạy migration trên database thật hoặc triển khai chức năng.




