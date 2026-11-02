# GaoApp Storefront Web

Website dành cho khách mua hàng, chạy thành tiến trình và triển khai riêng với
`GaoApp.Web`. Chỉ tham chiếu Application/Infrastructure và Domain qua các lớp
dùng chung; không tham chiếu project Web quản trị.

MVC/Razor riêng cho `/shop`, `/shop/products`, `/shop/products/{id}`,
`/shop/cart`, `/shop/checkout`, `/shop/orders`. WEB04 bổ sung báo giá có ưu đãi,
phí giao, xác nhận COD và tra cứu bằng liên kết riêng. Đặt COD ghi nhận đơn và
giữ hàng; thanh toán vẫn chưa thu và chưa có xuất kho vật lý.
Không có endpoint Admin/POS, cookie đăng nhập quản trị, worker, migration hoặc
seeder khi khởi động. Không tham chiếu controller, layout hay assets của GaoApp.Web.

Production vẫn đóng: `/` và `/shop` trả 503, các route khách và assets khác trả
404. `/health` luôn hoạt động mà không mở kết nối database. Thay cờ IsEnabled
trong database không vượt được cổng phát hành; mở Production thuộc WEB09.

Cấu hình bằng môi trường hoặc secret store của triển khai:

- `ConnectionStrings__DefaultConnection`: cùng nguồn dữ liệu GaoApp được chọn.
- `StorefrontHost__Host`: host công khai chính xác của cửa hàng.
- `StorefrontHost__StoreSubdomain`: mã subdomain chuẩn hóa của cửa hàng trong GaoApp.
- `StorefrontHost__UploadsRoot`: đường dẫn tuyệt đối tới **thư mục uploads vật lý**,
  không phải wwwroot. Ví dụ DB có `uploads/products/a.png`, file được đọc từ
  `<UploadsRoot>/products/a.png`. Không mount thư mục này bằng static files.
- `StorefrontHost__KeyRingPath`: thư mục tuyệt đối đã được triển khai tạo sẵn,
  riêng tư và bền vững để lưu Data Protection keys. Application name cố định
  `GaoApp.Storefront`; purpose receipt `GaoApp.Storefront.Placement.v1` + StoreId.
  Không đặt trong wwwroot, uploads, AppData, LocalAppData, `.aspnet`, root ổ đĩa
  hoặc đường dẫn reparse/symlink. Không ghi key vào thư mục mặc định của user.
  Thiếu/cấu hình không an toàn chặn checkout mới và replay receipt; browsing,
  anti-forgery tạm thời và guest snapshot không cần giải mã receipt vẫn hoạt động.
- `StorefrontAddressCatalog__Path`: đường dẫn tuyệt đối tới JSON địa chỉ cục bộ;
  khi không khai báo dùng file được đóng gói tại
  `Data/vn-address-catalog-observed-20261031.json` cạnh binary. File sai/thiếu
  chặn nhận đơn mới rõ ràng. Không tải danh mục từ mạng lúc chạy.

Preview chỉ chạy khi đồng thời thỏa các điều kiện: môi trường Development,
`StorefrontHost__PreviewEnabled=true`, địa chỉ bind và IP local/remote đều là
loopback, host/subdomain map đúng cửa hàng đang hoạt động, cấu hình website của
cửa hàng có IsEnabled=true khi đọc danh mục/nhận đơn mới. Khi ngừng nhận mới,
`/shop/orders`, POST `/shop/orders/access`, `/shop/checkout` (chỉ recovery),
POST `/shop/checkout/place` (kiểm tra receipt trước điều kiện nhận mới) và assets
riêng vẫn phục vụ bảo trì trong preview hợp lệ. Bind wildcard/LAN bị từ chối lúc khởi
động, kể cả Kestrel:Endpoints. Không dùng forwarded headers để thay IP nguồn.

Tester dùng database GUID và uploads fixture riêng, tự chuẩn bị cờ IsEnabled
trong fixture. Không bật cờ hoặc chạy preview bằng database cửa hàng thật.
Ví dụ cách chạy sau khi đã cấp cấu hình fixture qua môi trường:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:StorefrontHost__PreviewEnabled = 'true'
# ConnectionStrings__DefaultConnection, StorefrontHost__Host,
# StorefrontHost__StoreSubdomain và StorefrontHost__UploadsRoot do fixture cấp.
dotnet GaoApp.Storefront.Web.dll --urls http://127.0.0.1:5083
```

Media `/shop/media/{id}` kiểm tra publication và tenant mỗi lần đọc, chỉ trả loại
ảnh/glTF trong whitelist, có nosniff; chặn đường dẫn ngoài uploads root, file tạm,
file thiếu và reparse point/symlink. Uploads root không được phục vụ trực tiếp.
Viewer 3D và kiểm tra URI/phụ thuộc bên trong glTF thuộc WEB08; WEB03 không tự
fetch URI bên trong model.

Giỏ lưu version1 theo SHA256 của host+cửa hàng trong localStorage; chỉ lưu
ProductId/VariantId/UnitId/Quantity. POST `/shop/cart/resolve` là phép đọc có
anti-forgery, rate limit 120/phút/IP, body tối đa 32 KB và tối đa 100 lựa chọn.
Số lượng dương, tối đa 1.000.000 và bốn số lẻ. Mỗi dòng tạm tính được làm tròn
VND nguyên, AwayFromZero, rồi cộng; không phải báo giá/ủy quyền đặt đơn WEB04.
Giá và publication luôn đọc lại từ catalog chung; thay client price không có tác
dụng. Trạng thái cũ trong giỏ không ngăn xử lý snapshot của đơn đã tạo.

Inter Latin/Vietnamese được lấy từ hai file font đi cùng reference cục bộ; không
gọi CDN. Hồ sơ license/phân phối cuối được chốt trong WEB08 trước phát hành.

Không chép cấu hình ngân hàng hoặc connection string của tiệm vào source. Host
khác bị từ chối; client không chọn tenant bằng query hoặc cookie. Cấu hình thiếu
giữ website đóng; liveness không cần truy cập database.

Quản trị cấu hình vẫn tại `/admin/storefront/settings` trong `GaoApp.Web`; người
dùng tự thêm đường dẫn này bằng Quản lý menu. Migration chạy riêng bằng quy trình
của GaoApp, không chạy tự động từ website công khai.

WEB04 nhận JSON có anti-forgery header `RequestVerificationToken`, cookie
SameSite Strict, no-store và rate limit theo IP. POST `/shop/checkout/quote`
(30/phút, 48 KB) nhận tối đa 100 Items{productId,variantId,unitId,quantity}, tên,
điện thoại, ProvinceCode, WardCode, StreetAddress và Note. Tỉnh/phường chọn bằng
tên; máy chủ kiểm tra quan hệ code trong danh mục rồi tự chọn zone/method/phí,
không nhận giá, tenant, zone hoặc phương án giao rẻ từ client. StreetAddress do
khách nhập, không có geocoding hay xác minh vị trí. GET `/shop/checkout/wards`
(120/phút) trả phường/xã của tỉnh đã chọn.

Quote có hiệu lực 10 phút, chốt giá POS canonical, dùng cùng promotion engine,
cùng `OrderTotalsPolicy` và `DeliveryPricingPolicy.ApprovePos` của POS. Detached
Order chỉ dùng tính giá, Id/POSShiftId đều 0; chỉ line index cục bộ dương phục vụ
quan hệ quà. Không lưu POS order/shift/terminal/user giả. Chỉ các lựa chọn thực
mua hoặc sinh ra phải còn đăng bán; optional hidden item trong mixed pool không
tự loại chương trình. Tập ưu đãi sinh output không hợp lệ được loại và chạy lại
engine tối đa tám lần. Tối đa 200 dòng sau ưu đãi. `GrossAmount` đã bao gồm
`RoundingAdjustment`, `NetAmount = GrossAmount - Discount`; không cộng điều
chỉnh hai lần. UI dùng chuỗi tiền máy chủ, không tính số tiền qua JS Number.

POST `/shop/checkout/place` (20/phút, 4 KB) nhận ClientRequestId GUID,
QuoteToken GUID và Fingerprint. Dưới khóa StoreConfig, nguồn catalog/promotion/
kho/chủ thể được HOLDLOCK đến commit, khóa số dư dùng chung repository của POS;
giá/ưu đãi/điều kiện và hạn quote được kiểm tra lại sau khi chờ số dư. Thay đổi
trả QuoteChanged kèm quote mới, không có đơn hoặc hold; khách phải xác nhận mới.
Timeout/deadlock yêu cầu thử lại cùng key, không tự thử lại một phần transaction.

Một transaction lưu COD AwaitingPreparation/Unpaid, giá/địa chỉ/nguồn phân bổ
sealed, typed InventoryReservations ReferenceType12 và link, ReservedQty chung,
event/outbox OrderCreated và command receipt có outcome được bảo vệ. Source
physical chưa ghi ở WEB04; raw physical ReferenceType12 bị chặn service và DB.
Outbound cạnh tranh bảo vệ cả Delivery11 và Web12, giữ nguyên chính sách âm kho
cũ. Website chỉ dùng AllowNegative từ settings tin cậy cho quote/place/reserve.
Trigger/FK/check bảo vệ source identity, ngân sách giữ, giá và bản chụp đơn.
Migration Down chặn nếu đã có đơn/hold hoặc dữ liệu tiền/tên đơn vị sẽ mất nghĩa.

ClientRequestId lặp đúng nội dung mở lại đúng order/link kể cả sales closed,
address catalog lỗi hoặc product ẩn; cùng key khác payload trả409. Quote đã dùng
bằng key khác cũng409 và không trả private link. Pending proof chỉ chứa key,
quote/fingerprint và revision giỏ trong sessionStorage theo store. Mất phản hồi,
CSRF hoặc lỗi mơ hồ giữ proof; reload checkout có nút kiểm tra lại dù không còn
form nhận mới. Không tự sinh key khác khi chưa biết đơn đã được nhận hay chưa.
Xóa giỏ sau đặt chỉ khi signature/revision vẫn khớp, không ghi đè thay đổi đã
quan sát từ tab khác. localStorage compare/write không phải transaction liên tab.

Guest link dùng fragment `/shop/orders#<256-bit-token>`, chỉ token SHA256 lưu ở
order, raw token chỉ có trong outcome được Data Protection bảo vệ. Trang POST
token trong body tới `/shop/orders/access` (60/phút, 1 KB), không gửi token qua
URL request/referrer hoặc dùng phone/code để tra cứu. Link không tự hết hạn;
invalid/foreign/inactive-store trả404. Snapshot cũ không phụ thuộc sales enabled,
catalog/publication hay danh mục địa chỉ hiện tại. Không ghi token/PII vào log
chẩn đoán của module, không bật EF sensitive-data logging.

Inbox GaoApp tại `/admin/storefront/orders`, chi tiết `/{id}` và
`/notifications?afterId=...`, quyền `storefront.orders.view` ở controller/service.
Danh sách hiển thị số chờ chuẩn bị và poll 30 giây khi tab visible để báo đơn
mới; đây là đọc/thông báo, không đổi phase hoặc tồn kho. Người dùng tự thêm URL
bằng Quản lý menu. Không tạo menu, role grant hay sidebar cố định. Outbox bền
vững chưa gửi SMS/email ngoài. Fulfillment vật lý và hủy/hoàn thuộc WEB06–WEB07;
không mô phỏng đã giao hoặc tự chuyển khoản hoàn tiền.

## WEB05: QR và chứng cứ ngân hàng

Public host chỉ tạo intent/giữ hàng trong giao dịch đặt đơn và đọc trạng thái.
`POST /shop/orders/qr` và `/shop/orders/qr/image` nhận token trong body, dùng
CSRF/rate-limit như tra cứu đơn; không nhận account, amount, paid hay channel
từ khách. Mở lại, tải ảnh và poll luôn dùng intent cũ, không gia hạn hoặc sinh
mã mới. Không cần shop còn mở bán để xem đơn/QR cũ, nhưng QR quá hạn không
hiện ảnh hoặc cho khách sao chép lệnh chuyển mới.

Ngân hàng chạy ở host quản trị sở hữu khóa ACB cũ (`ApplicationName=GaoApp`,
purpose `GaoApp.Acb` + store). Guest keyring `GaoApp.Storefront` giữ độc lập.
Public DI không đăng ký transport, processor hay bank worker. Worker quản trị
chỉ chạy khi server cấu hình `StorefrontBanking:WorkerEnabled=true`; mặc định
thiếu cấu hình là tắt. Không đưa secret, raw evidence hoặc guest token vào log.

Mỗi store cần cấu hình server-local `StorefrontBanking:Registrations:{store}`:
Namespace=`W`, Provider=`ACB`, NamespaceVerified **mặc định false**, cùng
EvidenceReference, MerchantId, TerminalCode, UserCode, BankAccountId,
ReceivingLegalEntityId và AccountIdentity (SHA256 mã ngân hàng + `|` + tài khoản,
bỏ khoảng trắng, chữ hoa; giữ số 0 đầu và dấu câu). Chỉ đặt NamespaceVerified
sau khi người vận hành xác minh đăng ký thực tế với ngân hàng. Bộ mã nguồn và
fixture không chứng minh ngân hàng thật chấp nhận namespace hay mã kênh.
Hai host nhận cùng phần cấu hình đăng ký không bí mật; credential chỉ ở host
quản trị. Cấu hình thiếu/sai không tự đổi phương thức QR thành COD.

Không suy FT/GD/MGD từ điện thoại là transactionNumber của API. Tài khoản có
kênh điện thoại hoặc lịch sử chưa chứng minh mapping sẽ chặn QR mới; có thể
dùng tài khoản nhận riêng. `NotificationIdentityMappingVerified` cũng mặc định
false, chỉ bật với `NotificationIdentityMappingEvidence` sau xác minh độc lập
quan hệ định danh. Một quan sát thứ hai chưa chứng minh cùng hay khác giao dịch
được giữ nguyên số tiền trong nhóm IDENTITY-AMBIGUOUS, không cộng thành lần
thu mới. Không ghép giao dịch theo số tiền hoặc thời gian gần nhau.

Migration `20261031210000_AddStorefrontQrBankEvidence` chạy preflight trước DDL:
51160 chặn nâng cấp khi còn QR ACB cũ Pending/Creating/Received/Review chưa có
binding bất biến. Giải quyết các yêu cầu này bằng phiên bản cũ trước nâng cấp;
không tự gán tài khoản hiện tại vào chứng cứ lịch sử. Lịch sử completed/cancelled
không bị sửa. Downgrade 51161 chặn khi có reference, receipt, intent-processing,
observation hoặc liên kết tài chính mới. Không xóa chứng cứ để lách guard.

Callback chỉ là tín hiệu đã xác thực theo cơ chế ACB hiện có. Confirm tiền
dựa trên authenticated retrieve. Global inbox nhận tiền/protected evidence
commit trước giao dịch allocation; retry không làm mất khoản đã nhận. Registry
`W{globalSequence:D12}` không quay vòng, tối đa 999999999999. Số QR nguyên dương
không vượt int.MaxValue; merchant/tên người nhận tối đa 30 ký tự. Thay đổi tài
khoản/môi trường/routing có intent đang hoạt động bị chặn; đổi secret vẫn được.

Ngày ngân hàng lưu rõ Unknown / BankDay / Timestamp. BankDay là ngày Việt Nam
(UTC+07), không giả giờ nhận; timestamp có offset được chuẩn hóa UTC. Ngày lỗi
vẫn giữ số tiền/chứng cứ và Review. Lệch tài khoản nhận tường minh vào nhóm chưa
xác định quỹ, không tự gán cho tài khoản yêu cầu. Review là latch: không tự xóa
khi nhận lại phản hồi khớp. Khoản thực nhận xuất hiện trong Dòng tiền trước khi
chốt bán; mirror có FK nguồn được loại trùng. Giờ/CSV hiển thị đúng precision,
số dư ngân hàng chưa đủ căn cứ để trống có giải thích; tiền mặt giữ quy tắc cũ.

Độ tin cậy khoản thu (`AssetConfidence`) được chốt bất biến lúc nhận chứng cứ,
độc lập với lý do Review có thể thay đổi sau đó. Mặc định Unproven không được
cộng quỹ hoặc dùng thanh toán. Chỉ CertifiedDistinct mới có thể vào quỹ đúng
ngày và qua cổng phân bổ/mirror; chứng cứ lịch sử chưa chứng minh hoặc định danh
giao thức mơ hồ vẫn nằm nhóm đối soát dù có phản hồi mâu thuẫn mới. Phản hồi mới
không tự nâng độ tin cậy. Chứng cứ tài chính đã ghi nhận vẫn được kiểm tra khi
session hoặc transaction lịch sử đã xóa mềm; xóa mềm không chứng minh tiền chưa
từng dùng. Không sửa hoặc gán lại binding cho các bản chụp lịch sử này.

Staff có quyền `storefront.orders.view` xem `/admin/storefront/orders/{id}/bank-evidence`.
Đây là trang chỉ đọc; người dùng tự thêm URL qua Quản lý menu. Không tự tạo menu.
Giải quyết Review bằng nghiệp vụ được cấp quyền, hủy/hết hạn nhả giữ, dispatch
vật lý và sale mirror thật của đơn web thuộc các cổng tiếp theo; WEB05 không tạo
chứng từ xuất, ca POS giả hay doanh thu hoàn tất chỉ vì callback tiền.
