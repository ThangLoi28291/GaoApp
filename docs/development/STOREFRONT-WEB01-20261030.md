# WEB01 Dữ liệu nền và cấu hình website

Ngày: 30/10/2026. **WEB01 hoàn thành W01T01–W01T04 và host khách hàng nền; kiểm thử và review độc lập đạt sau sửa.** Website vẫn đóng. Nguồn nghiệp vụ: [hợp đồng WEB00, gồm bổ sung project riêng trong WEB01](STOREFRONT-WEB00-CONTRACT-20261030.md) và [kế hoạch](STOREFRONT-IMPLEMENTATION-PLAN-20261030.md).

## Hai project Web

Theo yêu cầu bổ sung của chủ cửa hàng, [GaoApp.Storefront.Web](../../GaoApp.Storefront.Web/GaoApp.Storefront.Web.csproj) là host khách hàng độc lập, không tham chiếu `GaoApp.Web`. Admin cấu hình vẫn nằm trong `GaoApp.Web`. Hai project dùng chung lớp nghiệp vụ và dữ liệu kho; có thể chạy/deploy riêng, không cần mở ca POS cho website hoạt động.

Host khách WEB01 chỉ có `/health`, `/` và `/shop` đang đóng; không có route quản trị/POS, cookie đăng nhập quản trị, worker, migration hoặc seeder khi startup. Host và mã cửa hàng được map từ cấu hình máy chủ, không cho client chọn qua query/cookie. Thiếu cấu hình giữ trạng thái đóng; liveness không cần database. Cách cấu hình nằm trong [README](../../GaoApp.Storefront.Web/README.md). Danh mục và checkout được làm ở các mốc kế tiếp.

## Phạm vi

- W01T01: cấu hình cửa hàng, nguồn kho, COD/QR, bán âm, kg và hạn chờ QR. Phí giao cố định về nghĩa `StoreServiceFee` cho cả giao gần/toàn quốc; chi phí trả shipper/hãng ghi riêng.
- W01T02: dữ liệu xuất bản theo sản phẩm/biến thể/đơn vị, kg và media tham chiếu hàng gốc. Mọi cờ đăng bán mặc định false; màn hình chọn hàng thuộc WEB02.
- W01T03: vùng giao, bảng phí có phiên bản và nền lưu quote/đơn/receipt/intent/giao/hoàn/ghi sổ/outbox. Checkout, nhận tiền, giữ/xuất kho và nghiệp vụ giao thật tiếp tục triển khai ở các mốc WEB04–WEB07.
- W01T04: một migration mới, snapshot tương ứng, FK gồm cửa hàng, unique/check/rowversion, quyền controller/service và tích hợp schema manifest. Migration không tạo menu hoặc gán quyền cho vai trò.

## Chức năng cấu hình

Đường dẫn thực: `/admin/storefront/settings`. Người dùng tự tạo mục menu bằng chức năng Quản lý menu nếu cần; không sửa sidebar hoặc dữ liệu `AdminMenuItems`.

| Quyền | Chức năng |
|---|---|
| `storefront.settings.view` | Xem cấu hình, các lựa chọn cùng cửa hàng và vùng giao |
| `storefront.settings.manage` | Lưu thông tin, kho, phương thức thanh toán và chính sách website |
| `storefront.shipping.manage` | Thêm/sửa/tạm tắt vùng giao và tạo phiên bản bảng phí |
| `storefront.products.view` / `.manage` | Khai báo nền quyền cho màn hình WEB02 |

Đăng nhập và policy được kiểm tra tại controller. Service kiểm tra lại người dùng, quyền hiện tại và cửa hàng lấy từ tenant; request không nhận `StoreId`, chủ thể kho hoặc quầy/ca từ client. Các lệnh POST có anti-forgery. Quyền quản lý không tự thay thế quyền xem; cấp các quyền phù hợp cho người thao tác trong màn hình Vai trò.

GET chưa có cấu hình trả về giá trị chuẩn bị, không chèn dữ liệu. Website mặc định đóng, COD được chọn sẵn, QR chưa bật, bán âm được chọn, chưa tự đặt thời hạn QR hoặc mức phí. `IsEnabled=true` bị service từ chối ở WEB01; cổng mở bán thuộc WEB09. Những số nhập trong test không trở thành cấu hình cửa hàng.

Logo nhận đường dẫn ảnh trong `/uploads/`, không nhận URL bên ngoài hoặc đường dẫn thoát thư mục. QR cần tài khoản ACB hoạt động, chủ thể nhận được chọn, quyền thu tiền cho nguồn hàng, cấu hình ACB sẵn sàng và định danh ngân hàng độc lập POS. Đây là chuẩn bị cấu hình, chưa chứng minh ngân hàng chấp nhận kênh web; WEB05 kiểm chứng kết nối.

Vùng giao dùng mã tỉnh/phường xã chuẩn hóa dạng mã, không suy địa giới bằng tên nhập tự do. Bản đầu nhận vùng Việt Nam; vùng toàn quốc để trống tỉnh/xã, giao gần bắt buộc phạm vi tỉnh; xã chỉ có khi có tỉnh. Một phạm vi/hình thức chỉ có một vùng để tránh hai bảng phí ngang ưu tiên. WEB04 xác thực địa chỉ từ danh mục địa giới chuẩn trước khi chọn vùng và báo giá; WEB01 không tải hoặc tạo dữ liệu địa giới giả.

Mỗi lần đổi bảng phí tạo revision mới; quote giữ tham chiếu bản cũ. Đổi chế độ kg cập nhật các bảng phí hiện hành bằng revision mới và làm cũ phiên bản chỉnh sửa vùng. Mọi lệnh cấu hình dùng cùng khóa SQL theo cửa hàng và rowversion; cả form tạo vùng mới cũng gắn phiên bản cấu hình đã xem để không âm thầm đổi nghĩa phí cố định sang kg. Người lưu từ màn hình cũ phải tải lại, không ghi đè dữ liệu mới.

## Dữ liệu và bảo vệ

Các entity mới ở [Domain/Storefront/Foundation](../../GaoApp.Domain/Storefront/Foundation/StorefrontFoundation.cs); [cấu hình EF](../../GaoApp.Infrastructure/Data/Configurations/StorefrontFoundationConfiguration.cs) được AppDbContext áp dụng cùng bộ lọc tenant hiện tại. WEB00 policy vẫn ở thư mục gốc Storefront và không đổi.

23 bảng mới gồm cấu hình/quyền thu tiền, sản phẩm/biến thể/đơn vị/media, vùng/bảng phí, quote/đơn/dòng, intent/receipt/phân bổ tiền, nguồn giao/nguồn dòng/liên kết giữ hàng, liên kết ghi sổ, hoàn/bằng chứng hoàn, command receipt/event/outbox.

FK nguồn gồm cửa hàng và đối tượng liên quan: biến thể phải đúng sản phẩm; đơn vị đúng biến thể; kho đúng chủ thể; dòng nguồn đúng đơn; link chứng từ bán phải đúng ca/người chốt. Quote và đơn buộc tổng tiền khớp; intent và allocation gắn đúng số phải thu, tài khoản/chủ thể và receipt xác thực. Các budget tổng nhiều dòng và hiệu ứng nghiệp vụ sẽ do adapter có khóa/transaction thực hiện ở WEB04–WEB07, không có API ghi tiền/kho trong WEB01.

Snapshot và bằng chứng có trigger chống sửa/xóa; các cột bảo vệ được so sánh theo byte và độ dài, kể cả đổi hoa/thường hoặc thêm khoảng trắng. Bảng phí chỉ thay `IsCurrent`, intent chỉ thay trạng thái hoạt động, receipt chỉ thay cờ cần đối soát và outbox chỉ thay thông tin retry/xử lý. Link delivery khớp cả kho/chủ thể và chỉ được gắn một lần. Lát dòng có transaction phải khớp SKU/kho/đơn giao và chứng từ xuất giao typed policy 2; null transaction vẫn là nền chưa có chứng cứ vật lý, không đủ để chốt bán. Dòng hàng và lát nguồn đã ghi giữ nguyên. Bằng chứng hoàn tách phần tiền hàng, phí giao và khoản thu chưa phân bổ để xử lý tiền thừa/đến muộn ở WEB07.

Nền nguồn giao chưa gắn giỏ/ca POS giả. `StorefrontFulfillmentSource` để link delivery trống cho đến khi WEB06 mở rộng origin của module giao hàng; `StorefrontReservationLink` tham chiếu sổ giữ chung, adapter loại nguồn sẽ bổ sung ở WEB04. Không thay nullable/guard nguồn POS hiện có trong WEB01.

## Kiểm tra và bằng chứng

Model trước WEB01 đã được so sánh snapshot: 0 khác biệt. Công cụ sinh migration chỉ chạy metadata, không mở kết nối SQL; từ chối thay đổi model ngoài bảng Storefront và các khóa unique chứa StoreId/Id để làm FK.

Migration mới: `20261030170000_AddStorefrontFoundation`, tạo 23 bảng và 75 index, thêm 7 khóa unique chứa StoreId/Id trên bảng nguồn hiện có để làm FK. Không đổi cột hoặc số tiền/tồn của POS. Down chặn nếu còn bất kỳ dữ liệu Storefront để tránh mất dữ liệu; permission và quyền người dùng đã cấp được giữ lại. Chưa áp migration lên database cửa hàng.

Schema manifest đã nhận diện chính xác các trigger mới. Khi kiểm tra toàn bộ prefix, phát hiện ba SQL bổ sung permission trong các migration tháng 10 hiện hữu chưa được manifest nhận diện; bổ sung whitelist theo SHA256 đúng ba statement đã đọc, không sửa các migration đó hoặc cho phép SQL/DDL tùy ý.

Kiểm tra trước bàn giao tester: hai project Web build đạt 0 lỗi/0 warning; model sau migration khớp snapshot (0 operation), schema manifest đọc đủ 197 bảng; bộ quy tắc WEB00 chạy lại đạt 378/378. Những số này chưa thay thế kết quả SQL/HTTP và review độc lập bên dưới.

Lượt độc lập ban đầu chạy 134 ca, 133 đạt và 1 lỗi strict schema comparison. Diagnostic tách được tên trigger chưa chuẩn hóa, các CHECK được SQL Server viết lại tương đương và ba filtered index lịch sử. Bản sửa giữ nguyên assert đầy đủ: chuẩn hóa tên, nhận diện đúng từng cặp predicate hoàn chỉnh và đúng ba filter đã xác minh. Literal SQL giữ nguyên byte/case/bracket/khoảng trắng để không che thay đổi pattern hoặc BIN2. Review độc lập cũng phát hiện bốn điểm cần siết: immutable text theo collation, link nguồn giao, link transaction kho và form vùng mới dùng cấu hình cũ. Đã sửa và kiểm thử lại các điểm này; giữ lịch sử các lượt lỗi trong artifacts.

Trong lúc build lại, output mặc định của Web quản trị bị tiến trình khác khóa DLL. Giữ nguyên tiến trình đó và chuyển build/test sang `.artifacts/storefront-web01/build-v2` với output riêng từng project; không dừng ứng dụng đang mở để chạy test.

Tester độc lập `web01_tests` đã ghi oracle trước khi đọc API, sở hữu các test `GaoApp.Tests/Storefront/Web01*.cs`. SQL test chỉ dùng database GUID của `InventoryPostingLocalDb`, có guard tên và cleanup. Đã lưu danh sách database test có trước để không dọn nhầm; không dùng dữ liệu cửa hàng hoặc fixture tự seed menu.

## Kết quả cuối V3

Tester `web01_tests` độc lập với người triển khai; reviewer `web01_review` độc lập với cả hai. Đã chạy test sau khi hoàn thành source, sửa các phát hiện, chạy lại toàn bộ và review lại trên đúng bytes cuối.

| Kiểm tra | Kết quả |
|---|---|
| WEB01: cấu hình/quyền/tenant/đồng thời | 57/57 |
| Vùng giao, bảng phí và phiên bản | 24/24 |
| Schema fresh/upgrade/reapply/downgrade | 20/20 |
| Commerce FK/unique/immutable | 41/41 |
| Nguồn giao/kho chính xác | 4/4 |
| Chuẩn hóa schema và mutation/default checks | 21/21 |
| HTTP host khách bằng process riêng | 3/3 |
| Tổng WEB01 | **170/170, 0 lỗi/0 bỏ qua** |
| Hồi quy schema hiện hữu được chọn | **8/8, 0 lỗi/0 bỏ qua** |
| Probe do reviewer tự dựng và chạy | **6/6 từ chối đúng, exit 0** |
| Review cuối | **Không còn P0/P1/P2 chưa xử lý trong phạm vi WEB01** |

SQL thực tế sau migration khớp đầy đủ manifest; không bỏ qua bảng/constraint/filter để đạt. Cả hai Web build thành công; graph test có 0 lỗi và một warning xUnit2031 có sẵn ở test ngoài WEB01. SDK 10.0.403, runtime 10.0.14, SQL Express 17.0.1145.4. Bộ thuần WEB00 chạy hồi quy riêng 378/378, không cộng thành số ca SQL/HTTP.

28 file production, 8 file test và 7 DLL giữ nguyên hash trong lượt cuối. SQL trước/sau giữ nguyên đúng 5 database test có trước, không để lại database GUID mới. Tester/reviewer chỉ dùng fixture GUID; không chạy migration lên database cửa hàng, ngân hàng hoặc đơn giao thật. Không commit/push/deploy.

Bằng chứng lưu trong repository: [tóm tắt test cùng lệnh, hash và cleanup](storefront-web01/validation-summary.json), [báo cáo review độc lập](storefront-web01/review-final.md). Log/TRX/probe đầy đủ nằm trong `.artifacts/storefront-web01/tests/` và `.artifacts/storefront-web01/review/`.

Lệnh chạy lại trên graph đã build:

```powershell
$env:GAOAPP_WEB01_PUBLIC_DLL = (Resolve-Path .artifacts/storefront-web01/build-v2/bin/GaoApp.Storefront.Web/debug/GaoApp.Storefront.Web.dll).Path
dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-build --no-restore --artifacts-path .artifacts/storefront-web01/build-v2 --filter 'Category=StorefrontWeb01'
```

Ranh giới: đã kiểm tra foundation SQL/service và HTTP của host khách. Chưa chạy trình duyệt/HTTP anti-forgery round trip của toàn Web quản trị, checkout, nhận tiền thật, giao thực tế, chốt tiền/kho, budget nhiều dòng hoặc UAT. Các phần này theo WEB02–WEB09. WEB02 chưa bắt đầu; chưa mở bán.
