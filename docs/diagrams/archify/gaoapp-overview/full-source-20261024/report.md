# GaoApp — Rà soát toàn source và bổ sung sơ đồ

Mở [mục lục đầy đủ](index.html), [tra cứu file/module](coverage.html), [156 controller](controllers.html), [bằng chứng source](evidence.html). Tổng hợp lúc lúc 19:58:20 GMT+7 Thứ Bảy, 24 tháng 10, 2026.

## Kết quả và phạm vi đầy đủ

Đã bổ sung **54 HTML Archify**: 35 architecture/module maps, 11 sequence, 3 lifecycle, 5 tree. Bộ tám sơ đồ G1/G2 trước được giữ nguyên bytes và liên kết lại từ mục lục mới.

- 36/36 nhóm source runtime có sơ đồ riêng. 2.333/2.333 file runtime và 156/156 controller được gán đúng một nhóm để tra cứu; kiểm tra không thiếu/không trùng.
- Kiểm kê 11487 đường dẫn; lập chỉ mục 3393 file first-party: 2333 runtime, 744 kiểm thử, 157 tool/script, 159 migration/snapshot. Có 907 khai báo public trong chỉ mục controller; đây không phải số endpoint runtime.
- Cây EF bao phủ 138 DbSet khai báo tường minh trong AppDbContext. Cây worker có 10 lời đăng ký AddHostedService theo host. Cây solution/công cụ định vị 17 csproj trong phạm vi active, gồm cả project ngoài solution.
- Đây là bao phủ cấu trúc và các luồng chính. Không khẳng định mọi dòng đã được đọc thủ công, mọi dispatch động/route convention đã được resolve hoặc mọi nhánh của mỗi method đã có Sequence. Inventory/call index dùng phân tích văn bản, không phải compiler call graph.
- Loại khỏi phân tích runtime: output build/cache, node_modules/packages, vendor/minified JS, binary/static media và bản sao/probe trong Logs. CSS, cấu hình và tài liệu có trong kiểm kê đường dẫn nhưng không biến thành cạnh gọi hàm. Không đọc cấu hình secrets để đưa giá trị vào tài liệu.

## Những sơ đồ được bổ sung

Các miền trước chưa có sơ đồ riêng gồm tài khoản/quyền/menu, catalog/barcode/supplier/customer, ca POS, trả hàng, cọc/công nợ, thu chi, reward/promotion, chuyển/kiểm/điều chỉnh kho, pháp nhân/kho, XML/thư viện hóa đơn đầu vào, ACB/QR/thông báo ngân hàng, display/kiosk, in ấn, media, trợ lý, CSKH/Zalo, audit, offline, realtime và báo cáo. Các miền mua–nhập–POS–giao–hóa đơn đã có lát cắt trước được mở rộng thành module map riêng.

| Nhóm source | File runtime | Controller |
|---|---:|---:|
| [Menu động và ẩn/hiện](module-menus.html) | 26 | 2 |
| [Tiếp nhận thông báo ngân hàng](module-bank-notifications.html) | 15 | 2 |
| [CSKH, hội thoại và kênh Zalo](module-customer-support.html) | 38 | 7 |
| [Trợ lý và ChatbotLocal](module-assistant.html) | 37 | 10 |
| [Offline và chống gửi trùng POS](module-offline.html) | 8 | 1 |
| [SignalR và giám sát cửa hàng](module-realtime.html) | 22 | 2 |
| [Barcode và quy đổi đơn vị](module-barcode.html) | 79 | 9 |
| [Yêu cầu mua và đơn đặt mua](module-procurement.html) | 53 | 2 |
| [XML và hóa đơn đầu vào](module-inputxml.html) | 78 | 3 |
| [Phiếu nhập và nghiệp vụ nhận hàng](module-receiving.html) | 139 | 11 |
| [Chuyển kho](module-stock-transfer.html) | 26 | 2 |
| [Kiểm kê](module-stock-count.html) | 25 | 2 |
| [Điều chỉnh tồn](module-stock-adjust.html) | 51 | 4 |
| [Trả hàng và nhập lại](module-returns.html) | 32 | 2 |
| [Ca/quầy POS và điều chỉnh thanh toán](module-shifts.html) | 108 | 15 |
| [Công nợ và tiền cọc khách hàng](module-customer-money.html) | 21 | 2 |
| [Thu chi và chi phí hoạt động](module-treasury.html) | 8 | 1 |
| [Điểm thưởng và voucher](module-rewards.html) | 54 | 3 |
| [Khuyến mãi](module-promotions.html) | 42 | 3 |
| [Giao hàng, soạn và bàn giao](module-delivery.html) | 67 | 6 |
| [QR và thanh toán ACB](module-acb.html) | 82 | 6 |
| [In bill, in tem và template](module-printing.html) | 46 | 3 |
| [Màn hình khách hàng](module-display.html) | 12 | 1 |
| [Kiosk tự phục vụ](module-kiosk.html) | 12 | 2 |
| [Pháp nhân và kho](module-legal-warehouse.html) | 70 | 5 |
| [Hóa đơn đầu ra và Viettel](module-invoices.html) | 246 | 8 |
| [Ghi sổ kho, định giá và giữ hàng](module-stock-core.html) | 152 | 4 |
| [Báo cáo và dashboard](module-reports.html) | 48 | 6 |
| [Upload và thư viện media](module-media.html) | 21 | 3 |
| [Hồ sơ khách hàng](module-customers.html) | 16 | 2 |
| [Nhà cung cấp](module-suppliers.html) | 22 | 2 |
| [Sản phẩm và danh mục](module-catalog.html) | 168 | 7 |
| [Audit và outbox](module-audit.html) | 34 | 2 |
| [Đăng nhập, tài khoản và quyền](module-auth.html) | 141 | 5 |
| [Bán hàng POS và đơn bán](module-pos.html) | 128 | 6 |
| [Khởi động, nền tảng và tiện ích chung](module-platform.html) | 206 | 5 |

## Kiến trúc thực tế và điểm dễ đọc sai

- Web chính là ASP.NET Core MVC/Razor/API và SignalR. Application chứa hợp đồng/service nghiệp vụ; Infrastructure có EF Core/repository/adapter và một số service giao hàng; Domain chứa entity/enum. Các module không mặc định là microservice. Framework hiện tại là .NET 10 theo project source.
- **SQL Server + SQLite + filesystem** cùng tồn tại. AppDbContext và AuditLogDbContext được cấu hình cùng DefaultConnection; hai context không có nghĩa hai database vật lý. CustomerServiceStore trực tiếp mở SQLite; trợ lý có JSON tri thức/cấu hình, media/thư viện chứng từ có adapter filesystem. Xem [data-stores](data-stores.html).
- API POST inventory/transactions tổng quát trả **410 Gone**. Việc inject InventoryMovementService vào controller không chứng minh endpoint còn ghi kho. Đường ghi qua chứng từ/POS chuyên biệt.
- ACB webhook xác thực, lưu inbox và ACK; worker xử lý receipt đến hạn trong tenant scope riêng. Không vẽ webhook trực tiếp hoàn tất thanh toán ngay lúc nhận.
- Chuyển kho thử source-preserving transfer; fallback mới tạo cặp TransferOut/TransferIn. Kiểm kê chỉ ghi chênh lệch khác 0, có nhánh gain/loss và resolve cost.
- Chuyển kho/kiểm kê cho phép gửi lại từ Rejected. Phiếu điều chỉnh chỉ Submit từ Draft, không có cạnh Rejected → PendingApproval; Approved bị chặn Cancel.
- CSKH Web nhận lượt rồi tùy RunAi/private/handoff mới gọi CustomerAiBridge. Kênh Zalo có runner/worker, guard quyền/cửa sổ phản hồi và trạng thái review khi kết quả chưa rõ. Các nhánh này dùng SQLite, không được gộp thành repository SQL Server của POS.
- Audit outbox dùng EventId/store, transaction/application lock và kiểm tra log tồn tại trước khi thêm; lỗi giữ pending/retry marker theo source.
- Upload lưu metadata trước bytes file, sau đó mới cập nhật token. Tìm kiếm trợ lý đọc dữ liệu theo store/permission, rank nội bộ rồi hydrate lại; không coi Rank là lời gọi AI.
- XML dùng SelectAsync → thư viện file → ImportAndLinkAsync, kiểm tra supplier/owner và liên kết phiếu. Không coi UploadXmlAsync legacy là endpoint Web đã được chứng minh đang gọi.
- Sidebar vẫn là AdminMenuItems theo store/quyền/trạng thái/thứ tự; không tạo menu, seeder hoặc dữ liệu trong tác vụ.

## Source evidence và validation

- **PASS — native static gates**: 54/54 validate/deliver/strict check, mỗi sơ đồ 9/9 kiểm tra showcase, 0 lỗi và 0 composition warning. Candidate/HTML SHA-256 khớp receipt hiện hành. Không gộp warning browser-skipped vào kết luận hình học.
- **PASS — HTML audit**: 54 SVG/HTML so với candidate, source modes, ID, edge metadata, 4 trang tra cứu, liên kết và checksum. [html-audit.json](html-audit.json).
- **PASS — logic audit**: 135 ca parse inline JavaScript, control presence, bộ lọc bằng DOM fixture, thứ tự câu chuyện và khác biệt vòng đời. [logic-audit.json](logic-audit.json). Không chạy native Viewer trong browser.
- **PASS — source integrity**: 450 phạm vi trích dẫn từ 162 file giữ nguyên hash và khoảng dòng hợp lệ; 3393/3393 file đã lập chỉ mục giữ nguyên từ snapshot inventory. [source-integrity.json](source-integrity.json).
- 36 sơ đồ có native Sources ghim HEAD; 18 sơ đồ có ít nhất một file/anchor chỉ có trong WT hoặc khác HEAD. Loại sau dùng file/dòng/hash và card WT, không gắn repository/native Sources sai. Strict artifact provenance không được diễn giải thành HEAD source verification. [evidence.json](evidence.json).
- **NOT RUN — browser/visual**: policy URL trước đó chặn file://. Không dùng đường vòng/browser khác để né chặn. ARCHIFY_CHROME chỉ được đặt cho tiến trình tới đường dẫn không tồn tại nhằm ngăn browser stage khởi chạy, không thay đổi cấu hình dự án. Mọi finalize mới trả exit 2, browser skipped và ok:false. Vì vậy tổng trạng thái **PARTIAL_VALIDATION_BROWSER_NOT_RUN**, không phải full PASS.
- Chưa click kiểm tra thực zoom/focus/Route/Lens, mở rộng Tree, đổi theme/export hoặc cuộn cuối Sequence mới. Không có screenshot/perceptual review mới. Bằng chứng browser G1 chỉ áp dụng cho bytes G1 cũ.
- **NOT RUN — nghiệp vụ**: không build/chạy Web/worker/migrator, tests fixture, SQL, import, schema repair, in vật lý hoặc API ngoài. Không tuyên bố đúng tiền/kho, an toàn đồng thời, schema production hay tích hợp đang hoạt động.

| Sơ đồ | Loại | Source | Artifact | Browser | Bằng chứng hiện hành |
|---|---|---|---|---|---|
| [Menu động và ẩn/hiện](module-menus.html) | architecture | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/module-menus/module-menus.finalize-summary.json) |
| [Tiếp nhận thông báo ngân hàng](module-bank-notifications.html) | architecture | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/module-bank-notifications/module-bank-notifications.finalize-summary.json) |
| [CSKH, hội thoại và kênh Zalo](module-customer-support.html) | architecture | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-1/module-customer-support/module-customer-support.finalize-summary.json) |
| [Trợ lý và ChatbotLocal](module-assistant.html) | architecture | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-1/module-assistant/module-assistant.finalize-summary.json) |
| [Offline và chống gửi trùng POS](module-offline.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-offline/module-offline.finalize-summary.json) |
| [Barcode và quy đổi đơn vị](module-barcode.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-barcode/module-barcode.finalize-summary.json) |
| [Yêu cầu mua và đơn đặt mua](module-procurement.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-procurement/module-procurement.finalize-summary.json) |
| [XML và hóa đơn đầu vào](module-inputxml.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-inputxml/module-inputxml.finalize-summary.json) |
| [Phiếu nhập và nghiệp vụ nhận hàng](module-receiving.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-receiving/module-receiving.finalize-summary.json) |
| [Chuyển kho](module-stock-transfer.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-stock-transfer/module-stock-transfer.finalize-summary.json) |
| [Kiểm kê](module-stock-count.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-stock-count/module-stock-count.finalize-summary.json) |
| [Điều chỉnh tồn](module-stock-adjust.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-stock-adjust/module-stock-adjust.finalize-summary.json) |
| [Trả hàng và nhập lại](module-returns.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-returns/module-returns.finalize-summary.json) |
| [Ca/quầy POS và điều chỉnh thanh toán](module-shifts.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-shifts/module-shifts.finalize-summary.json) |
| [Công nợ và tiền cọc khách hàng](module-customer-money.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-customer-money/module-customer-money.finalize-summary.json) |
| [Thu chi và chi phí hoạt động](module-treasury.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-treasury/module-treasury.finalize-summary.json) |
| [Điểm thưởng và voucher](module-rewards.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-rewards/module-rewards.finalize-summary.json) |
| [Khuyến mãi](module-promotions.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-promotions/module-promotions.finalize-summary.json) |
| [Giao hàng, soạn và bàn giao](module-delivery.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-delivery/module-delivery.finalize-summary.json) |
| [QR và thanh toán ACB](module-acb.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-2/module-acb/module-acb.finalize-summary.json) |
| [In bill, in tem và template](module-printing.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-printing/module-printing.finalize-summary.json) |
| [Màn hình khách hàng](module-display.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-display/module-display.finalize-summary.json) |
| [Kiosk tự phục vụ](module-kiosk.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-kiosk/module-kiosk.finalize-summary.json) |
| [Pháp nhân và kho](module-legal-warehouse.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-legal-warehouse/module-legal-warehouse.finalize-summary.json) |
| [Hóa đơn đầu ra và Viettel](module-invoices.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-invoices/module-invoices.finalize-summary.json) |
| [Ghi sổ kho, định giá và giữ hàng](module-stock-core.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-stock-core/module-stock-core.finalize-summary.json) |
| [Báo cáo và dashboard](module-reports.html) | architecture | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/module-reports/module-reports.finalize-summary.json) |
| [Upload và thư viện media](module-media.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-media/module-media.finalize-summary.json) |
| [Hồ sơ khách hàng](module-customers.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-customers/module-customers.finalize-summary.json) |
| [Nhà cung cấp](module-suppliers.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-suppliers/module-suppliers.finalize-summary.json) |
| [Sản phẩm và danh mục](module-catalog.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-catalog/module-catalog.finalize-summary.json) |
| [Audit và outbox](module-audit.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-audit/module-audit.finalize-summary.json) |
| [Đăng nhập, tài khoản và quyền](module-auth.html) | architecture | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-1/module-auth/module-auth.finalize-summary.json) |
| [Bán hàng POS và đơn bán](module-pos.html) | architecture | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-pos/module-pos.finalize-summary.json) |
| [SignalR và giám sát cửa hàng](module-realtime.html) | tree | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-1/module-realtime/module-realtime.finalize-summary.json) |
| [Khởi động, nền tảng và tiện ích chung](module-platform.html) | tree | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-1/module-platform/module-platform.finalize-summary.json) |
| [Mô hình EF Core theo DbSet](ef-model.html) | tree | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-1/ef-model/ef-model.finalize-summary.json) |
| [Các nơi lưu dữ liệu thực tế](data-stores.html) | architecture | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/data-stores/data-stores.finalize-summary.json) |
| [Pipeline đăng ký xử lý request Web](web-request-pipeline.html) | sequence | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-2/web-request-pipeline/web-request-pipeline.finalize-summary.json) |
| [Worker và tác vụ nền](hosted-workers.html) | tree | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/hosted-workers/hosted-workers.finalize-summary.json) |
| [Solution, kiểm thử, CI và công cụ](projects-tests-tools.html) | tree | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-4/projects-tests-tools/projects-tests-tools.finalize-summary.json) |
| [Webhook ACB: lưu inbox rồi xử lý nền](sequence-acb-inbox.html) | sequence | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-2/sequence-acb-inbox/sequence-acb-inbox.finalize-summary.json) |
| [CSKH: nhận tin, gọi AI và chuyển người hỗ trợ](sequence-customer-chat.html) | sequence | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/sequence-customer-chat/sequence-customer-chat.finalize-summary.json) |
| [Kênh CSKH / Zalo: polling và dispatch có điều kiện](sequence-customer-channels.html) | sequence | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/sequence-customer-channels/sequence-customer-channels.finalize-summary.json) |
| [Xác nhận chuyển kho](sequence-stock-transfer.html) | sequence | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/sequence-stock-transfer/sequence-stock-transfer.finalize-summary.json) |
| [Vòng đời phiếu chuyển kho](lifecycle-stock-transfer.html) | lifecycle | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-2/lifecycle-stock-transfer/lifecycle-stock-transfer.finalize-summary.json) |
| [Xác nhận kiểm kê](sequence-stock-count.html) | sequence | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/sequence-stock-count/sequence-stock-count.finalize-summary.json) |
| [Vòng đời phiếu kiểm kê](lifecycle-stock-count.html) | lifecycle | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-2/lifecycle-stock-count/lifecycle-stock-count.finalize-summary.json) |
| [Upload media: metadata trước, bytes sau](sequence-media-upload.html) | sequence | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-2/sequence-media-upload/sequence-media-upload.finalize-summary.json) |
| [Trợ lý tìm sản phẩm theo scope và dữ liệu mới](sequence-assistant-search.html) | sequence | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-2/sequence-assistant-search/sequence-assistant-search.finalize-summary.json) |
| [Vòng đời phiếu điều chỉnh tồn](lifecycle-stock-adjust.html) | lifecycle | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-3/lifecycle-stock-adjust/lifecycle-stock-adjust.finalize-summary.json) |
| [Audit outbox: xử lý theo store và EventId](sequence-audit-outbox.html) | sequence | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-3/sequence-audit-outbox/sequence-audit-outbox.finalize-summary.json) |
| [Đăng nhập, store, thiết bị và cookie](sequence-login.html) | sequence | WT manifest | PASS 9/9 | SKIPPED | [receipt](review-3/sequence-login/sequence-login.finalize-summary.json) |
| [Chọn XML từ thư viện và liên kết phiếu nhập](sequence-input-xml.html) | sequence | HEAD + WT | PASS 9/9 | SKIPPED | [receipt](review-3/sequence-input-xml/sequence-input-xml.finalize-summary.json) |

## Git trước và sau

- Source/Git root: E:\GaoApp\GAOAPP-SA-20260819-01 / E:/GaoApp/GAOAPP-SA-20260819-01.
- Branch: codex/delivery-d04-d12-20261006. HEAD: 4495ffb7ba7cbd0a5d58507a1bf702398d0a8465; giữ nguyên.
- Ngoài thư mục artifact: trước 790 tracked / 8189 untracked; sau 790 / 8189. Status giống: true. Diff stat giống: true.
- Diff trước:  782 files changed, 81656 insertions(+), 7263 deletions(-). Sau:  782 files changed, 81656 insertions(+), 7263 deletions(-).
- Dòng status mới ngoài artifact: 0; dòng mất: 0. Status/stat không xác định tác giả hoặc chứng minh mọi bytes trong repository bất biến. Không dọn/restore thay đổi của người khác.
- [Status trước](git-status-before.txt), [status sau](git-status-after.txt), [diff trước](git-diff-before.txt), [diff sau](git-diff-after.txt), [staged stat sau](git-staged-diff-after.txt), [đối chiếu Git](git-comparison.json).
- Theo thao tác của tác vụ: chỉ ghi artifact dưới docs/diagrams/archify/gaoapp-overview/, cập nhật hai mục lục cũ và manifest artifact. Không sửa source ứng dụng, DB, migrations, solution/project, AGENTS/BMAD hoặc tài liệu workflow. Không commit/push/refactor/đổi cấu hình. Level A documentation; không tuyên bố TASK PASS hay Independent Review.

## Giới hạn còn lại

Không có ERD vật lý đầy đủ cột/FK của database đang chạy. Cây 138 DbSet không bao phủ tự động owned/shadow types hoặc context khác; các type/configuration được tra trong chỉ mục. Không chạy cơ sở dữ liệu để suy đoán bảng/cột. Module maps là lời gọi tĩnh tiêu biểu và dependency đã định vị, không phải mọi đường xử lý/overload hoặc mọi nhánh nghiệp vụ. Trạng thái hoạt động của cấu hình tích hợp/worker và topology triển khai chưa xác minh.

Mật khẩu, token, connection string và bản ghi khách hàng thật không được đưa vào sơ đồ. Chỉ có tên cấu hình, định danh class/method/file, khoảng dòng và hash.

## Danh sách artifact

[files-created.txt](files-created.txt) liệt kê các file của phần bổ sung này. [Manifest toàn bộ](../files-created.txt) bao gồm các bộ cũ và mới. Hai file mục lục cũ được cập nhật: ../index.html và ../g2/index.html; tám native HTML cũ giữ nguyên bytes.
