# WEB09 — chuẩn bị vận hành public, chưa phát hành

Trạng thái mặc định: **CLOSED — chưa publish, chưa triển khai, chưa mở cửa hàng thật**. Đây là hướng dẫn kỹ thuật và hồ sơ nghiệm thu chờ thực hiện, không phải biên bản chủ cửa hàng đã ký. Dự án public độc lập là `GaoApp.Storefront.Web`; không chạy Main, seeder, migrator hay công cụ publish ERP để thử website.

## Các cổng độc lập

1. Máy public: Production + `StorefrontHost:ReleaseEnabled=true`, đúng host/store cố định, HTTPS thật và khóa riêng hợp lệ. `PreviewEnabled` chỉ dành Development trên loopback. Hai cờ mặc định false; không dùng header `X-Forwarded-Proto` để biến HTTP thành HTTPS. Phạm vi được chuẩn bị là Windows/.NET10, Kestrel TLS hoặc IIS in-process theo cấu hình đã được kiểm chứng; không tự thêm reverse-proxy trust.
2. Admin: `StorefrontRelease:AllowSettingsActivation=true` cho phép người đang có quyền điều khiển DB intake. Cờ này mặc định false khi không khai báo. Admin không xác minh được DPAPI identity của máy public và không thể tuyên bố website đã được phát hành.
3. Cửa hàng: `StorefrontSettings.IsEnabled=true` sau thao tác có quyền, phiên bản mới và kiểm tra cấu hình hiện tại. Cờ DB không vượt cổng public. Dừng intake là điều khiển vận hành thông thường; không dùng thay đổi cổng server làm thao tác dừng hằng ngày.

Không cờ nào là xác nhận UAT hoặc ngân hàng thật. Ví dụ cấu hình ở `docs/development/storefront-web09` đều đóng. Không chép thông tin bí mật vào kho mã hoặc gói web.

## Cấu hình và khóa bảo vệ

- Hostname exact và `StoreSubdomain` của cửa hàng được chọn từ cấu hình server; query/cookie/posted StoreId không chọn tenant. Chuẩn hóa host không làm mất phân biệt cửa hàng. Chọn đúng hostname trong `AllowedHosts` khi triển khai sau này.
- Kết nối SQL, uploads và keyroot phải được cung cấp bằng cấu hình riêng của máy, ngoài gói public. Keyroot tuyệt đối, riêng, không qua reparse point, ngoài webroot/uploads/thư mục mặc định người dùng; không chép key vào output/deployment archive.
- Giữ nguyên ApplicationName `GaoApp.Storefront`, keyroot, Windows service identity và profile. Cấp ACL riêng qua quy trình hạ tầng được duyệt, không dùng quyền rộng để vượt lỗi. IIS cần profile của identity thực; chạy dưới tài khoản khác không phải bằng chứng khóa cũ còn mở được.
- Chọn `KeyProtection=WindowsDpapiCurrentUser` trên Windows. Khóa ghi mới dùng framework DPAPI CurrentUser; marker khóa cũ chỉ chứng minh envelope DPAPI, không chứng minh phạm vi lịch sử. Framework phải mở được **mọi** khóa cũ, không chỉ Protect/Unprotect bằng khóa hiện tại.
- Protected mode tắt automatic generation; chỉ sau khi toàn bộ ring được xác thực mới cho framework tạo/luân chuyển khóa. Plaintext/unsupported/không giải mã được/mất envelope từng thấy sẽ khóa ghi và giữ process đóng. Không tự sửa, xóa, adopt, re-encrypt hay thay ring để hết lỗi. Khôi phục đúng ring/identity và khởi động lại sau kiểm tra.
- Bootstrap ring rỗng chỉ khi cờ server ReleaseEnabled được tin cậy và truy vấn đúng cửa hàng/schema chứng minh chưa có bất kỳ đơn (kể cả archived), placement proof hoặc QR intent cũ. Có lịch sử mà ring rỗng → `KEYRING_MISSING_PROOF`; không ghi khóa mới. Ring khác nhưng hợp lệ riêng nó vẫn phải mở được placement outcomes cũ với đúng purpose. Không có đủ proof liên tục → giữ đóng để phục hồi có bằng chứng.
- Không chạy scanner/đọc nội dung XML bí mật để chẩn đoán. Normal framework repository/decryption là đường kiểm tra; readiness không trả XML, ID khóa, đường dẫn hay exception. Không cleanup ring cũ trong WEB09.

## Kiểm tra trước khi bật

`GET /health` chỉ kiểm tra process sống. `GET /health/ready` phải dùng đúng host/HTTPS thực, trả các mã cố định và `runtimeReady`/`acceptingNewOrders` riêng biệt; không trả thông tin bí mật. Runtime có thể ready200 khi cửa hàng đang dừng nhận đơn; `acceptingNewOrders=false` vẫn là chặn intake. HTTP200 không thay thế UAT.

Chạy offline `scripts/storefront/Test-StorefrontPreparation.ps1` với template đóng hoặc cấu hình operator được giữ riêng. Script chỉ kiểm tra cấu trúc/source/package metadata, không gọi SQL/ngân hàng, không đọc key contents, không publish, không bật cờ. `OFFLINE_CHECKS_PASSED` không phải runtime PASS. Trường hợp `OperatorConfiguration` phải vẫn được đối chiếu bằng actual readiness và nghiệm thu sau này.

Các blocker kích hoạt gồm: `ACTIVATION_POLICY_CLOSED`, `HOST_STORE_BINDING_INVALID`, `BRANDING_CONTACT_INVALID`, `SOURCE_OWNER_INVALID`, `PUBLICATION_MISSING`, `PUBLICATION_UNIT_PRICE_WEIGHT_INVALID`, `SHIPPING_ZONE_RATE_INVALID`, `PAYMENT_METHOD_MISSING`, `SHIPPING_TREATMENT_INVALID`, `QR_BINDING_REGISTRATION_INVALID`. Sửa tại đúng chức năng, tải lại phiên bản rồi thử. QR kiểm tra cấu hình/namespace/account/authority hiện tại; không phải kết quả kết nối live bank. COD-only không bị ép bật QR/3D. Mỗi báo giá/đặt đơn vẫn kiểm tra nguồn, tiền, hạn mức và quyền thu thực tế riêng.

## Đường dẫn thật và quyền

Không tự tạo menu. Người dùng có thể thêm các URL sau bằng Quản lý menu; sidebar vẫn do cấu hình cửa hàng và quyền quyết định.

| Công việc | Đường dẫn | Quyền/điều kiện |
|---|---|---|
| Cấu hình và điều khiển intake | `/admin/storefront/settings` | `StorefrontPermissions.ViewSettings`; bật/dừng cần `ManageSettings`, active membership, CSRF |
| Bật/dừng POST | `/admin/storefront/settings/activate`, `/stop-new-orders` | Direct `Version` canonical8B + `Reason`1–1000; recheck dưới lock |
| Hàng đăng bán | `/admin/storefront/products`, `/admin/storefront/products/{productId}` | ViewProducts/ManageProducts |
| Chọn media | `/admin/storefront/products/media`, `/admin/media-library` | Quyền media/publication hiện có; không ép có3D |
| Soạn/giao/thu tiền | `/admin/storefront/fulfillment` | Quyền vận hành tương ứng; nguồn/ca và bằng chứng thật |
| Hủy/hoàn tiền | `/admin/storefront/refunds` | Quyền cancellation/refund hiện có; xác nhận execution cần quyền Treasury và actual debit |
| Khách | `/shop`, `/shop/products`, `/shop/cart`, `/shop/checkout`, `/shop/orders` | Scope public + capability token riêng; token trong body, không dán token vào báo cáo |

Trang hàng hiện có: dùng đường dẫn có `productId` từ danh sách; không đoán SKU/owner/media thay khách hàng. Đường dẫn báo cáo thu chi/lợi nhuận dùng chức năng hiện hữu theo menu cửa hàng, không suy quyền từ tên URL. Theo dõi hàng chờ/thiếu/incident/returns, bank Review/unattributed/undated, hoàn còn nợ, invoice work và provisional cost từ các màn hình thật; không relabel hoặc ACK để làm bảng theo dõi xanh.

## Bật/dừng, lỗi và tiếp tục đơn cũ

Bật kiểm tra toàn bộ điều kiện hiện tại dưới cùng configuration lock với checkout; `IsEnabled`, `Revision` và AuditLog thay đổi atomically. Lưu toàn bộ form settings vẫn dừng intake; sau lưu phải dùng thao tác bật riêng. Cùng phiên bản đã ở trạng thái đích là no-op không audit/revision mới; bật vẫn kiểm tra policy/config trước ACK. Phiên bản sai/stale409 → tải lại, xem trạng thái, không tự ghi đè. Phiên bản malformed hoặc thiếu lý do400.

**Dừng** chỉ cần quyền hiện tại + phiên bản, không đòi sửa cấu hình QR/phí/hàng không liên quan. Không viết lại stale form, credential, warehouse, authority hoặc rate. Dừng không hủy đơn, không trả holds, không chuyển tiền, không xóa nợ hay làm mất cờ Review.

Sau dừng: new quote/new key đặt đơn bị từ chối. Placement đã commit và retry cùng key/body vẫn trả kết quả gốc, không tạo thêm đơn/hold/intent. Khách còn tra cứu/QR/hủy bằng capability cũ; image QR đơn đã hủy không được mời trả tiếp. Admin vẫn soạn/giao/return/refund theo quyền và proof. Bank worker/invoice worker ở Admin process riêng tiếp tục xử lý đơn/tiền cũ theo cờ worker hiện hữu; không thêm worker tài chính vào public.

Khi có lỗi khóa/DB/host, đừng bật cờ để vượt. Giữ intake đóng, ghi mã lỗi cố định, thời điểm, build/source seal và trạng thái nghiệp vụ không chứa PII/secret; chuyển người phụ trách. Late QR money của đơn hủy vẫn phải được giữ trong ledger/Review/refund liability, không mở lại đơn. Outgoing refund không tự chuyển: chỉ xác nhận actual CashOut/Treasury debit thật đã có và đủ proof/budget.

## Chuẩn bị gói, nâng cấp và phục hồi

WEB09 **không chạy `dotnet publish` hoặc Publish targets**. Public .csproj import riêng `eng/deployment/StorefrontPublishSafety.targets`: gói tương lai dùng template đóng, loại live appsettings/private artifacts và không nhận entrypoint Admin/migrator/worker. Static SDK và final publish filter đều được chặn; giữ dependency DLL/native SkiaSharp, local Three/Inter licenses, catalog nguồn. Không dùng generic ERP publisher vì không phải public packaging contract. Actual package output/hosting còn UNVERIFIED cho đến một lần phát hành được cho phép riêng.

Chưa thêm migration/schema WEB09. Xem lịch sử migration thật trước nâng cấp, dùng migrator được quản lý và backup có kiểm chứng. Không chạy migration live ở bước chuẩn bị. Rehearsal độc lập dùng DB GUID riêng và backup path riêng được SQL service cho phép; kiểm tra cả backup/restore thật sang DB khác, guest capability, key continuity, original receipt replay, late bank recovery và tiền/hàng trước/sau. Không dùng Main/GaoAppDb hoặc phục hồi đè DB đang hoạt động.

`New-StorefrontRecoveryPlan.ps1` chỉ tạo worksheet PENDING, không thực thi SQL. Ghi backup point/RPO/RTO thật và mọi sự kiện sau backup; không tuyên bố zero data loss nếu restore điểm cũ. Khi đã có proof tài chính, ưu tiên forward repair/code rollback tương thích schema. Không Down vượt guard, không xóa đơn/receipt/holds/claim để rollback; code cũ không hiểu nguồn mới thì không được mở intake. Giữ ring đúng chỗ, không export/copy private XML như một phần gói/backup bằng script này. Việc sao lưu key riêng phải có quy trình/bảo mật/ủy quyền khác.

## Phát hành còn giữ

Chủ cửa hàng cần ký worksheet UAT với hàng thật, chủ thể/giá/đơn vị/kg/phí/media được duyệt, COD/QR/soạn/giao/return/hủy/actual-refund, dừng/mở và phục hồi. Domain/TLS/Windows identity/ACL, kết nối/callback live bank, phone thật, telemetry field, Linux nếu được chọn và operator RPO/RTO vẫn là các prerequisite riêng. Không điền PASS bằng fixture Windows Chromium. WEB09 technical pre-publication acceptance, nếu đạt, vẫn không cho phép publish/triển khai/mở thật trái chỉ đạo người dùng.
