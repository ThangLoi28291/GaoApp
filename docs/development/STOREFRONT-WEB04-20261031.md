# WEB04 — Báo giá và đặt COD không cần tài khoản

Ngày: 31/10/2026. Trạng thái: **PASS — production V3/test V6, 640 test ID riêng biệt đạt, không bỏ qua; review độc lập và kiểm tra identity/cleanup cuối đạt.** Không còn P0/P1/P2 trong phạm vi WEB04. Production chưa mở bán.

Phạm vi: W04T01–W04T04 theo [plan](STOREFRONT-IMPLEMENTATION-PLAN-20261030.md) và [hợp đồng WEB00](STOREFRONT-WEB00-CONTRACT-20261030.md). Production do `storefront_code`, oracle/test độc lập do `storefront_tests`, review/probe độc lập do `storefront_review`; root điều phối, build và lưu hồ sơ. Không mở WEB05 trước khi test và review bước này cùng đạt.

## Chức năng trong bước

- Website khách tiếp tục chạy riêng trong `GaoApp.Storefront.Web`; dùng chung Domain/Application/Infrastructure với quản trị.
- Máy chủ báo giá theo nguồn giá lẻ, engine khuyến mãi hiện có và nguyên phần tính tổng được tách từ POS. Hàng tặng, combo, mixed quantity, giảm và phân bổ/làm tròn dùng engine/policy chung. Chỉ lựa chọn thực tế mua/tặng cần hợp lệ trên website; thành phần tùy chọn bị ẩn nhưng không được mua không làm mất một chương trình mixed quantity hợp lệ.
- Phí giao cố định hoặc theo kg, ngưỡng miễn phí và vùng phục vụ được tính từ cấu hình. Mã tỉnh/phường được xác minh bằng danh mục máy chủ; client không quyết định StoreId, giá, phí, vùng hoặc nhãn giao gần. Tiền ship là `StoreServiceFee`; chi phí shipper/hãng thuộc bước nghiệp vụ giao hàng riêng.
- Form mobile chọn tên tỉnh/phường, thông tin nhận hàng và ghi chú; hiển thị hàng, đơn vị, ưu đãi, phí/tổng trước khi xác nhận. Quote đổi hoặc hết hạn trả báo giá mới để khách xác nhận lại; chưa tạo đơn hoặc lượng giữ từ kết quả này.
- COD hợp lệ tự vào `AwaitingPreparation`, thanh toán `Unpaid`. Một giao dịch ghi đơn/dòng/source snapshot, giữ hàng chung, receipt chống trùng, event và outbox. Giữ hàng tăng `ReservedQty`, không đổi tồn vật lý. Bán âm theo cấu hình website; bảo toàn các lượng giữ POS/giao hàng khác và điều kiện bán âm hiện có của các luồng đó.
- Gửi lại đúng request nhận lại cùng đơn và liên kết riêng từ receipt được bảo vệ bằng Data Protection. Khác payload hoặc dùng lại quote đã đặt dưới request khác trả conflict; không phát capability của lệnh cũ cho request mới.
- Tra cứu bằng token ngẫu nhiên 256 bit, lưu hash trong đơn; liên kết dùng fragment `/shop/orders#<token>`, request đọc gửi token trong JSON body. Không dùng số điện thoại/mã đơn để mở lịch sử. Token COD không tự có TTL; đây là quyền đọc riêng dài hạn cho đơn, vẫn cần đúng hash/cửa hàng hoạt động.
- GaoApp có inbox, chi tiết, số đơn chờ chuẩn bị và banner/poll báo đơn mới. Controller và service kiểm tra quyền `storefront.orders.view`. Người dùng tự tạo menu đến `/admin/storefront/orders`; không có menu/sidebar/role grant tự tạo.

## Dữ liệu, tiền và kho

Migration `20261031180000_AddStorefrontCheckout` bổ sung tham chiếu typed hold, FK tenant/order/line/variant/source/warehouse/owner, khóa duy nhất, proof triggers và snapshot bất biến. `InventoryReferenceType.Storefront=12` chỉ dùng giữ hàng; service và CHECK chặn giao dịch kho vật lý tự gắn nguồn 12. Xuất giao thực tế sẽ dùng nguồn Delivery11 có bằng chứng ở WEB06. Chưa tạo physical `StorefrontLineSource`, POS order/shift/cart giả hoặc chứng từ thu tiền.

Giá đơn vị giữ nguyên từ POS. `DeliveryPricingPolicy.ApprovePos` xử lý chênh lệch làm tròn tổng bằng điều chỉnh nguyên có giới hạn; dòng lưu `RoundingAdjustment`, Net = Round(qty × price) + adjustment − discount, tổng Net bằng tiền hàng. DTO Gross đã gồm adjustment. UI dùng chuỗi tiền từ máy chủ, giữ đúng chữ số vượt khả năng biểu diễn nguyên của JavaScript. Tạm tính giỏ WEB03 chưa phải báo giá đặt hàng có ưu đãi/làm tròn.

Danh mục địa chỉ được lấy từ [tra cứu Cục Thống kê](https://danhmuchanhchinh.nso.gov.vn/DiaBan1.aspx), bản quan sát 31/10/2026: 34 tỉnh/thành và 3.321 xã/phường, UTF-8 287.104 bytes, SHA256 `688F1A761199A2315C3C3B0F68F642493A961D775E5ADA2233411291D518A23D`. Reviewer dùng parser riêng đối chiếu đủ 34 raw page hashes, tên/mã tỉnh và 3.321 quan hệ phường/tỉnh. Chỉ JSON/provenance được đóng gói; không tải danh mục khi khách đặt, không sao chép script/HTML nguồn vào website. Bản này không xác minh vị trí số nhà/đường hoặc bảo đảm cập nhật hành chính tương lai.

## Các lỗi đã nhận định và sửa

| Mã | Nhận định | Sửa và kiểm chứng |
|---|---|---|
| W04R1 | Giá/trạng thái có thể đổi khi Place đợi khóa kho vì nguồn cũ không dùng storefront applock | Khóa rows/ranges nguồn đến commit; đọc lại sau balance locks/expiry; rollback trước quote thay thế. Ca SQL admin đổi giá/factor/active/thêm-kích hoạt promotion đã chạy trong nhóm critical |
| W04R2 | Xuất POS cấm âm chưa tính hold website vào ngân sách bảo vệ | Thêm Web12 với Delivery11, giữ nguyên nhánh bán âm và own-delivery exclusion; ca FIFO thực kiểm tra counter/ledger |
| W04R3 | Enum hold12 mới có thể đi vào request xuất kho generic | Chặn service và SQL CHECK; physical web dành cho typed Delivery11 bước sau |
| W04R4 | Quote đã dùng với request khác nổ unique exception | Kiểm tra binding và trả 409 hữu ích; không trả guest link cho key mới |
| W04R5 | Trùng tên biến trong manifest catalog làm build lỗi CS0136 | Đổi riêng hai biến; build lại đạt |
| W04R6 | SQL datetime2 đưa ra JSON không có UTC suffix làm browser lệch 7h | Gắn UTC ở projection mới, giữ ticks; browser Bangkok cuối đã xác nhận đúng 02:48:34 ngày 31/10 cho timestamp canonical |
| W04R7 | Bốn biểu thức CHECK tương đương bị so khác sau SQL Server lưu | Thêm bốn cặp Source/Persisted toàn biểu thức chính xác; giữ DDL/literal/grouping/flags. Schema thực khớp hoàn toàn, thêm positive/idempotence/semantic-mutation/flag/literal tests |

Lỗi fixture giữ riêng: tên bảng ProductVariants thay vì ProductVariant; mã DeliveryOrders quá giới hạn; planned original coverage của root picking line không đúng ràng buộc; thiếu DeliveryPickingWork cha. Các bản sửa chỉ dựng lại nguồn thử hợp lệ, giữ các assertion kho/tiền/quyền và dữ liệu lỗi ban đầu để đối chiếu. Ca thực bảo toàn POS3 + Delivery4 + Web3 = ReservedQty10 đã chạy đạt sau khi graph hợp lệ.

## Kiểm thử cuối

Kết quả cuối: **640 test ID riêng biệt đạt, 0 bỏ qua** — 207 WEB04, 170 WEB01, 138 WEB02, 51 WEB03 và 74 ca POS/promotion/schema/inventory liên quan. Bốn TRX cuối ghi 641 lượt: một lượt metadata bị cấu hình R1-only không phù hợp chặn trước tạo connection, cùng test ID đó chạy lại đạt sau khi bỏ override thừa. Guard và source không đổi; selector thử cục bộ có thể tiếp tục dùng SQLEXPRESS, không tuyên bố đã mở LocalDB. Ca metadata không Open/Migrate/EnsureCreated. Reviewer đối chiếu đủ ID, không trùng giữa 566 ca chính và 74 ca bổ sung.

Lượt critical trước sửa cuối: 43/45 đạt; schema parity và fixture tên bảng còn lỗi. Sau sửa: targeted 41/41 đạt, gồm schema không còn sai khác, 37 ca normalization bổ sung và browser UTC/giỏ/quote/đặt/khôi phục/tiền lớn. Targeted và các lượt lỗi lịch sử không cộng vào 640. Các lỗi fixture tiếp theo được sửa riêng; ca giữ hàng POS3/Delivery4/Web3 đạt trước lượt full cuối. Reviewer xác nhận đóng W04R1–W04R7.

Canonical browser GUID: `798ff960c9514e5c884f662d13c2bb51`; 10 bước, 36 ảnh, 33 trạng thái đo viewport/font/contrast và một trạng thái bàn phím. Ba lần gửi Place gồm một lần QuoteChanged trên quote cũ, rồi một cặp accepted/lost-response và retry có body giống nhau; không phải cả ba cùng lệnh. Reviewer đối chiếu raw timestamp và ảnh ở cả năm chiều rộng.

Production V3 gồm 51 tệp, test V6 gồm 18 tệp đã khóa hash; root build tách ở `.artifacts/storefront-rollout/build`. Sau chạy và dọn, 51 + 18 + 6 DLL + 9 bản sao dependency runtime = 84 identity checks, 0 sai lệch. Snapshot 69 file cuối ở `.artifacts/storefront-rollout/WEB04/coordinator/frozen-v3-test-v6`. Bằng chứng, các lượt fail, TRX, raw SQL definitions, browser và review nằm trong `.artifacts/storefront-rollout/WEB04/`.

Trước khi tái sử dụng output build cho WEB05, coordinator đối chiếu lại 15 DLL chính/dependency với hash cuối WEB04 và lưu bản sao ở `coordinator/final-binaries-v3-test-v6`, cùng manifest: 31 tệp gồm các DLL đã khóa và PDB/deps/runtimeconfig được chụp bổ sung. Đây là lưu bằng chứng binary, không phải bộ triển khai chạy độc lập; không sao chép appsettings, keyring hoặc credential.

Đã dọn 478 thư mục GUID thuộc fixture/799 tệp, gồm 215 khóa riêng thử và 12 tệp media tổng hợp; giữ 169 log/metadata, không lưu bản sao vật liệu khóa. Ba gốc runtime/media thử trống, không còn 56 PID thử hoặc host/Node/Chrome headless khớp harness. Danh sách master trước/sau giữ đúng năm database GUID có sẵn, cùng SHA256; không đọc/ghi database cửa hàng hoặc dừng tiến trình chưa xác định.

Hồ sơ lưu trong repository: [test độc lập](storefront-web04/test-final.txt), [review độc lập](storefront-web04/review-final.md), [kết quả distinct](storefront-web04/test-results.json), [browser](storefront-web04/browser-summary.json), [identity](storefront-web04/identity.json), [cleanup](storefront-web04/test-cleanup-summary.json), [findings đã đóng](storefront-web04/findings.json), [audit địa chỉ](storefront-web04/address-audit.md). Restore/build dùng output riêng và NuGet.Config offline của validator; test dùng `--no-build --no-restore` trên chính DLL đã hash, category StorefrontWeb04, StorefrontWeb01–03 và tập legacy đã audit không đi qua Main/bootstrap/FullApplicationFixture. SQL/discovery/browser chỉ dùng quyền cần thiết của công cụ với fixture GUID SQLEXPRESS.

## Vận hành và ranh giới

Production vẫn mặc định đóng. Development preview giữ điều kiện cờ rõ ràng, loopback bind/peer/local và host→subdomain máy chủ. Cần key ring bền vững trong thư mục riêng đã cấu hình `StorefrontHost__KeyRingPath`; không tự ghi vào user AppData/.aspnet. Catalog địa chỉ dùng bản đóng gói hoặc `StorefrontAddressCatalog__Path` tuyệt đối. Mất cấu hình chặn nhận mới; link đọc đơn cũ/inbox độc lập, receipt replay cần đúng key ring.

Pending intent lưu trong sessionStorage theo cửa hàng. Network/CSRF/409/503 chưa rõ kết quả giữ nguyên request; trang checkout tạm đóng vẫn có “Kiểm tra lại đơn vừa gửi”. QuoteChanged không tự đặt lại. Chỉ xóa phần giỏ còn khớp để không ghi đè thay đổi ở tab khác.

Đường dẫn: `/shop/checkout`, `/shop/checkout/wards`, `/shop/checkout/quote`, `/shop/checkout/place`, `/shop/orders`, `/shop/orders/access`, `/admin/storefront/orders` và notifications dưới URL quản trị đó. Cổng WEB04 đã đạt, được chuyển WEB05. QR là WEB05, chuẩn bị/giao/ghi sổ WEB06, hủy/hoàn WEB07, 3D/giấy phép font và trải nghiệm cuối WEB08, nghiệm thu/phát hành WEB09. Không có chuyển tiền hoặc mở bán thật trong WEB04.
