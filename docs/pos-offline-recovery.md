# Xử lý client POS offline không đồng bộ

## HTTP 500 khi đồng bộ mã hàng có SKU dài — 09/10/2026

Ảnh POS tại `chonthanh.gaomart.com.vn:8443` báo **Máy chủ trả lỗi HTTP 500 khi đồng bộ** và còn 31 thao tác. File `gao-pos-pending-1-1791522235248.json` có ngữ cảnh store `1`, terminal `1`, user `641758`, shift `800786`. Thao tác đầu là `c21a77dd-8ba1-4f92-84f7-6e1d06a84b02`, `POST /admin/pos/cart/current/scan`, barcode `8934988560459`, quantity `1`, giỏ server `1836550`.

Sản phẩm variant `553265` có SKU `nuoc-giat-xa-power-huong-nuoc-hoa-phap-dam-me-tui-3kg`, dài **53 ký tự**. `ProductVariants.Sku` cho phép 60 ký tự, nhưng `OrderLines.Sku` trước sửa chỉ cho phép 50. POS sao chép nguyên SKU vào dòng hàng; SQL Server từ chối ghi với lỗi truncation (8152/2628), API trả HTTP 500. Client giữ nguyên thao tác chưa được xác nhận và gửi tuần tự, nên 30 thao tác sau tiếp tục chờ. Nhãn “Mất kết nối máy chủ” cũng được dùng khi server trả 5xx; ảnh này xác định server trả lỗi, không chứng minh mất Internet.

File giữ bốn đơn đã hoàn tất cục bộ: `1836550` (230.000), `1500000186` (41.300), `1500000191` (29.100), `1500000196` (74.000). Các mã từ `1500000000` là mã tạm trên client. `state.conflict = null` vì nhánh 5xx giữ hàng đợi để tự thử lại; không có thao tác số lượng bất thường hay khoản thu bị cô lập trong file này. Tổng tiền mặt khách đưa có thể lớn hơn tổng đơn vì cần trả tiền thừa.

Sửa trong source: `OrderLineConfiguration` và snapshot tăng SKU lên 60; migration **`20261009060000_WidenOrderLineSku`** chỉ mở rộng cột `OrderLines.Sku` từ `nvarchar(50)` thành `nvarchar(60) NULL`. Không cắt SKU, đổi mã sản phẩm hoặc xóa journal. Down từ chối thu hẹp nếu đã có SKU dài hơn 50 đơn vị UTF-16.

Khôi phục tại cửa hàng:

1. Giữ file JSON và dữ liệu trình duyệt. Không xóa IndexedDB, tạo lại bốn đơn hoặc bỏ thao tác quét đầu tiên: các khoản thu/chốt đơn sau phụ thuộc vào sản phẩm này.
2. Chạy [SQL chỉ đọc](pos-sku-capacity-diagnostic.sql) trong database của host để xác nhận SKU/cột thực tế, ca/giỏ và receipt của thao tác. Nếu cần đối chiếu log, tìm `POST /admin/pos/cart/current/scan` quanh 11:49–12:08 ngày 09/10/2026 (UTC 04:49–05:08), dùng `traceId` trong phản hồi lỗi. File log mặc định là `App_Data/Logs/web-20261009*.log`; host có thể cấu hình vị trí khác.
3. Triển khai Web/Migrator phù hợp và áp dụng migration SKU theo TH2 trong [runbook triển khai](development/GAOAPP-DEPLOYMENT-RUNBOOK-20260913.md). Kiểm tra history thực tế để biết các migration còn thiếu; không chạy toàn bộ migration khác chỉ để xử lý lỗi SKU. Chỉ đổi JavaScript hoặc tăng thời gian chờ sẽ không sửa lỗi ghi cột này.
4. Mở đúng quầy/nhân viên/ca gốc, chỉ một tab POS, bấm **Thử đồng bộ**. Giữ mã thao tác cũ để server dùng receipt chống trùng. Chờ pending giảm từ 31 về 0; nếu phát sinh lỗi nghiệp vụ mới, lưu bản đối soát mới trước khi xử lý.
5. Đối chiếu bốn đơn, tiền thu/tiền thừa và tồn kho trên server trước khi đóng ca. Không nhập lại khoản thu đã được server xác nhận.

Đã tái hiện HTTP 500 trên Web/SQL thử nghiệm với chính SKU 53 ký tự và với SKU 60 ký tự trước sửa. Việc triển khai và kiểm tra database/log host phải được xác nhận riêng; file JSON không chứa exception SQL từ host.

Xác minh sau sửa: Release build đạt, 0 warning/error; **17/17 test đạt, 0 skip**. Gồm SKU 53/60 ký tự qua API scan offline, gửi lại cùng operation ID không thêm trùng dòng, tái hiện SQL truncation trên cột cũ, nâng cột giữ dòng hàng cũ, chặn downgrade mất dữ liệu, hồi quy POS/QR/khoản thu và kiểm tra model/snapshot/manifest/nâng cấp database. Evidence: `Logs/pos-sku-20261009/tests-before/before-unsandboxed.trx` (2/2 tái hiện HTTP 500 trước sửa), `Logs/pos-sku-20261009/tests-after/after.trx` (12/12), `Logs/pos-sku-20261009/tests-schema/schema-regression.trx` (5/5). Chưa deploy, chạy migration trên host hoặc xác nhận 31 thao tác đã đồng bộ tại cửa hàng.

## Trường hợp trước: hàng đợi có số lượng quá lớn

File đối soát của client có hàng đợi 353 thao tác. Thao tác đầu tiên cập nhật dòng hàng với `qty=138935001002581`, vượt giới hạn an toàn của POS. Client cũ coi lỗi HTTP từ server như lỗi mất mạng, giữ nguyên thao tác đầu hàng đợi và lặp lại mãi nên các thao tác hợp lệ phía sau không thể chạy.

## Phần đã sửa trong bản phát hành

- POS client từ chối số lượng nhỏ hơn hoặc bằng 0 và lớn hơn 1.000.000 khi thêm hàng hoặc sửa số lượng.
- POS server kiểm tra cùng giới hạn trước khi ghi dữ liệu vào cơ sở dữ liệu.
- Khi mở lại hàng đợi cũ, client nhận diện thao tác số lượng bất thường trong URL, dữ liệu POST và bản ghi đơn cục bộ trước khi gửi lên server.
- Nếu server từ chối một khoản thu không tiền mặt vì số tiền lớn hơn số còn phải thu (`overpay`), banner hiển thị nút **Cô lập khoản thu lỗi**. Khoản thu được chuyển vào `quarantined`, giữ nguyên toàn bộ nội dung để đối soát, còn hàng đợi hợp lệ tiếp tục được gửi.
- Với thao tác số lượng sai, banner hiển thị nút **Cô lập thao tác lỗi** với cùng cơ chế bảo toàn dữ liệu.

## Các bước triển khai và khôi phục client hiện tại

1. Sao lưu file JSON gốc và không chỉnh sửa hoặc xóa IndexedDB của máy client. Cần giữ đúng file `gao-pos-pending-1-1790861593285.json` để đối soát sau này.
2. Build và triển khai bản server có mã sửa lỗi. Sau khi triển khai, mở lại trang POS bằng `Ctrl+F5` để tải JavaScript mới; nếu có cache proxy/CDN thì xóa cache asset hoặc tăng version asset.
3. Đăng nhập đúng cửa hàng, đúng máy POS, đúng nhân viên và đúng ca đã tạo hàng đợi. Với file hiện tại, ngữ cảnh là store `1`, terminal `1`, user `641758`, shift `800746`. Không mở thêm tab POS trên cùng máy trong lúc khôi phục.
4. Vào màn hình POS và chờ banner hiển thị **Có thao tác offline cần đối soát**. Bấm **Lưu bản đối soát** trước khi thay đổi hàng đợi.
5. Bấm **Cô lập thao tác lỗi**. Client chỉ chuyển thao tác đầu tiên có `qty=138935001002581` vào danh sách đối soát; các đơn, khoản thu và thao tác hợp lệ vẫn giữ nguyên.
6. Bấm **Thử đồng bộ**. Chờ đến khi số thao tác chờ giảm về 0 hoặc banner báo một lỗi nghiệp vụ khác. Không đóng trình duyệt, xóa dữ liệu trang hoặc tạo ca mới trong lúc hàng đợi còn pending.
7. Khi đồng bộ xong, kiểm tra các đơn đã hoàn tất, tổng tiền, khoản thanh toán và tồn kho trên server. Đối chiếu riêng thao tác bị cô lập để xác định người dùng đã nhập nhầm số lượng hay cần khôi phục thủ công.
8. Nếu banner báo **ca hoặc nhân viên đã thay đổi**, dừng thao tác và đăng nhập lại đúng ca gốc. Không bấm đồng bộ cưỡng bức khi chưa khớp ngữ cảnh.

## Kết quả mong đợi với file hiện tại

Thao tác lỗi đầu tiên được nhận diện theo URL `/admin/pos/lines/1759520?qty=138935001002581`. Thao tác tiếp theo đặt số lượng `13` sẽ được xử lý sau khi thao tác đầu tiên được cô lập. Số pending phải giảm dần; nếu vẫn đứng yên ở cùng `operationId`, hãy bấm **Lưu bản đối soát**, giữ file xuất ra và chuyển log cùng mã thao tác đó để kiểm tra.

Nếu banner hiện `Phương thức này không cho phép thanh toán dư (overpay)`, hãy bấm **Lưu bản đối soát**, sau đó bấm **Cô lập khoản thu lỗi**. Khoản thu bị server từ chối sẽ được giữ trong `quarantined`; cần đối chiếu số tiền thực nhận và ghi lại khoản thu đúng trên server theo quy trình kế toán trước khi đóng ca.

## Không nên làm

- Không xóa toàn bộ IndexedDB hoặc localStorage để “làm sạch” hàng đợi.
- Không sửa trực tiếp file JSON rồi nhập lại nếu chưa có quy trình đối soát được phê duyệt.
- Không gửi lại thủ công tất cả đơn từ đầu; có thể tạo trùng đơn hoặc trùng khoản thu.
