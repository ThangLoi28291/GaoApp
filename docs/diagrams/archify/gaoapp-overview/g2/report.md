# GaoApp — Đào sâu module nghiệp vụ (G2)

Theo lựa chọn tiếp theo của người dùng: **Đào sâu sơ đồ các module nghiệp vụ**. Đây là phần mở rộng sau G0/G1; báo cáo G1 được giữ như bằng chứng lịch sử.

## Tài liệu đã hoàn thiện

Mở [mục lục nghiệp vụ](index.html). Bốn sơ đồ HTML native Archify tương tác:

- [GaoApp · Các điểm nối giữa module nghiệp vụ](business-modules.html): Bảy module/điểm vào và bảy lời gọi nối mua hàng, phiếu nhập, kho, POS, giao hàng và hóa đơn.
- [GaoApp · Từ PO đến duyệt và ghi sổ phiếu nhập](procurement-receiving.html): Tách request tạo Draft từ PO với request duyệt PendingApproval và ghi sổ kho.
- [GaoApp · Bàn giao sau giao hàng và chốt bán](delivery-handover.html): Hàng trả có điều kiện, chốt lượng thực giao, lưu settlement/outbox rồi commit ngoài cùng.
- [GaoApp · Worker phát hành Viettel theo nhánh Single](invoice-issuance.html): Nhánh Single: lưu claim bền, reserve/Issuing, gọi Viettel và ghi kết quả operation.

[Hướng dẫn lần theo source](source-tour.html) gồm 5 hành trình (menu, POS, hóa đơn Single, in qua server, in client) và bảng 9 project/ProjectReference. Các HTML native G1 giữ nguyên bytes.

## Kiến trúc và đường nối đã xác minh

- Backend Web chính là ASP.NET Core MVC/Razor/API + SignalR; Application chứa hợp đồng/service; Infrastructure triển khai EF Core/repository/adapter và một số service giao hàng; Domain chứa entity/enum. SQL Server là provider được cấu hình trong source. Worker hóa đơn, dịch vụ in máy chủ và client in có host riêng.
- PurchaseOrdersController gọi PurchaseOrderService.ApproveAsync và trực tiếp gọi StockDocumentService.CreateReceiptFromPurchaseOrderAsync. Không vẽ cạnh PurchaseOrderService → StockDocumentService khi callsite đó nằm ở controller.
- Tạo receipt từ PO lưu Draft trong transaction riêng. ApproveTrackedAsync yêu cầu PendingApproval (Confirmed là nhánh retry trả về), RowVersion và điều kiện kho/giá; nhánh hợp lệ gọi CreatePurchaseReceipt → InventoryMovementService.CreateAsync, rồi lưu Confirmed/commit.
- POS thường gọi inventory theo nhánh kho/pháp nhân. Callback sau commit gọi IInvoiceService.GenerateInvoicesFromOrderAsync; InvoiceService chuyển tiếp sang InvoiceCommandService. Sinh InvoiceHead nội bộ khác phát hành lên Viettel.
- Bàn giao xử lý hàng trả có điều kiện; có lượng Delivered > 0 mới gọi IDeliverySaleFinalizer.FinalizeDeliverySaleAsync. Command ngoài cùng commit receipt/settlement/revision/outbox, rồi RunDeferredActionsAsync. Snapshot/Ack được dựng trong command trước commit, trả kết quả sau hậu xử lý.
- AutoInvoiceWorker quét theo timer 5 giây; settings/lịch/provider quyết định xử lý. Nhánh Single lưu AutoInvoiceOperation/nguồn claimed và commit trước HTTP provider; service phát hành reserve/đánh dấu Issuing trước gọi client, rồi ghi trạng thái. Không suy ra mọi lỗi đều được gửi lại.
- Sidebar đọc AdminMenuItems theo cửa hàng/quyền/trạng thái/thứ tự. Tác vụ không tạo menu, seeder hoặc sửa quyền.

## Source, Git và phạm vi ghi

- Source root/Git root: E:\GaoApp\GAOAPP-SA-20260819-01 / E:/GaoApp/GAOAPP-SA-20260819-01.
- Branch: codex/delivery-d04-d12-20261006. HEAD: 4495ffb7ba7cbd0a5d58507a1bf702398d0a8465; so với đầu G2: giữ nguyên.
- Source WT dùng .NET 10, khác baseline HEAD ở một số vị trí; không coi WT dirty là bytes HEAD. Các Sources trong native Viewer ghim HEAD, cards/bảng ghi tọa độ WT.
- Kiểm tra SHA-256 và khoảng dòng: **PASS**, 126 phạm vi từ 50 file duy nhất; xem [source-integrity.json](source-integrity.json).
- Git ngoài thư mục artifact: trước 790 tracked / 8188 untracked; sau 790 / 8189. Status giống: false; diff stat giống: true.
- Diff stat trước:  782 files changed, 81656 insertions(+), 7263 deletions(-). Sau:  782 files changed, 81656 insertions(+), 7263 deletions(-).
- Có 1 dòng status mới và 0 dòng mất ngoài artifact. Status/stat không xác định tác giả hoặc chứng minh toàn source bất biến; chỉ các file được trích dẫn có đối chiếu hash. Không restore/dọn thay đổi của người khác.
- [Status trước](git-status-before.txt), [status sau](git-status-after.txt), [diff trước](git-diff-before.txt), [diff sau](git-diff-after.txt), [staged diff sau](git-staged-diff-after.txt), [đối chiếu máy đọc được](git-comparison.json).
- Mọi thao tác ghi của tác vụ nằm trong docs/diagrams/archify/gaoapp-overview/: file G2 mới, cập nhật mục lục và manifest artifact. Không sửa source ứng dụng, migration, DB, csproj/sln, AGENTS/BMAD; không build/chạy app/test nghiệp vụ, phát hành hóa đơn, gửi job in, refactor hoặc đổi cấu hình. Không commit/push.

## Validation và bằng chứng

| Sơ đồ mới | Validate | Deliver | Strict check | Browser | Receipt hiện hành |
|---|---|---|---|---|---|
| business-modules | PASS | PASS | PASS | SKIPPED, exit 2 | [summary](review-3/business-modules/business-modules.finalize-summary.json) |
| procurement-receiving | PASS | PASS | PASS | SKIPPED, exit 2 | [summary](review-3/procurement-receiving/procurement-receiving.finalize-summary.json) |
| delivery-handover | PASS | PASS | PASS | SKIPPED, exit 2 | [summary](review-3/delivery-handover/delivery-handover.finalize-summary.json) |
| invoice-issuance | PASS | PASS | PASS | SKIPPED, exit 2 | [summary](review-3/invoice-issuance/invoice-issuance.finalize-summary.json) |

- **PASS — artifact tĩnh:** Archify showcase, native source/provenance và ba gate đầu đạt cho 4 HTML mới. Các candidate/HTML được đối chiếu SHA-256 với receipt hiện hành; receipt cũ trong thư mục review trước chỉ là lịch sử.
- **PASS — HTML/SVG ngoại tuyến:** 8 sơ đồ, 50 node, 81 cạnh; so candidate với metadata render, SVG XML, ID, link, hash và đường có hướng. [html-audit.json](html-audit.json).
- **PASS — code/logic ngoại tuyến:** 35 ca; parse inline JavaScript, kiểm tra presence control, thử tìm kiếm bằng DOM fixture và thứ tự timeline. Strict check 8/8. [logic-audit.json](logic-audit.json). Không thực thi native Viewer trong browser.
- **NOT RUN — browser/visual:** trình duyệt tích hợp từ chối file:// theo chính sách URL. Không đổi transport/trình duyệt để mở bằng đường vòng. ARCHIFY_CHROME chỉ đặt theo tiến trình tới đường dẫn không tồn tại nhằm ngăn browser stage khởi chạy; không cài/cập nhật hoặc sửa cấu hình dự án. Vì vậy receipt native ghi skipped, exit 2, ok:false. Không tuyên bố G2 toàn bộ PASS.
- Chưa có bằng chứng click thực cho zoom, focus, Route/Lens, đổi theme, export hoặc cuộn cuối Sequence mới. Bằng chứng browser G1 là lịch sử của bốn HTML G1 giữ nguyên; không áp dụng sang HTML mới.
- Không có ảnh chụp/render review G2; kiểm tra bố cục hiện tại là đo tĩnh của Archify. Không suy ra chất lượng hiển thị thực tế chỉ từ cú pháp hợp lệ.

## Bằng chứng WT / HEAD cho sơ đồ mới

Manifest [business-evidence.json](business-evidence.json) gồm 43 phạm vi. HEAD bất biến: 4495ffb7ba7cbd0a5d58507a1bf702398d0a8465.

| Mã | File:dòng WT | Dòng HEAD |
|---|---|---|
| autoCycle | GaoApp.Application/Services/Invoices/AutoInvoiceService.cs:238–268 | 237–267 |
| autoDispatch | GaoApp.Application/Services/Invoices/AutoInvoiceService.cs:892–912 | 888–908 |
| autoRepo | GaoApp.Infrastructure/Repositories/Invoices/AutoInvoiceRepository.cs:543–545 | 541–543 |
| deliveryDispatch | GaoApp.Infrastructure/Services/Delivery/DeliveryInventoryService.cs:312–319 | 293–300 |
| deliveryReturn | GaoApp.Infrastructure/Services/Delivery/DeliveryInventoryService.cs:378–388 | 340–350 |
| deliverySale | GaoApp.Application/Services/Orders/POSService.Delivery.cs:25–43 | 25–43 |
| deliverySaleEnd | GaoApp.Application/Services/Orders/POSService.Delivery.cs:135–156 | 135–156 |
| handover | GaoApp.Infrastructure/Services/Delivery/DeliveryTripService.Handover.cs:168–199 | 168–199 |
| handoverReturn | GaoApp.Infrastructure/Services/Delivery/DeliveryTripService.Handover.cs:209–217 | 209–217 |
| handoverSale | GaoApp.Infrastructure/Services/Delivery/DeliveryTripService.Handover.cs:235–260 | 235–260 |
| inventoryCaller | GaoApp.Application/Services/Orders/POSService.cs:1026–1052 | 1026–1052 |
| invoice | GaoApp.Application/Services/Invoices/InvoiceCommandService.cs:77–113 | 77–113 |
| invoiceCallback | GaoApp.Application/Services/Orders/POSService.cs:209–235 | 209–235 |
| invoiceFacade | GaoApp.Application/Services/Invoices/InvoiceService.cs:38–44 | 38–44 |
| invoiceWrite | GaoApp.Application/Services/Invoices/InvoiceCommandService.cs:183–187 | 183–187 |
| issueBridge | GaoApp.Application/Services/Invoices/ViettelInvoiceIssueService.cs:211–223 | 211–223 |
| issueReserve | GaoApp.Application/Services/Invoices/ViettelInvoiceIssueService.cs:322–371 | 322–371 |
| issueState | GaoApp.Application/Services/Invoices/ViettelInvoiceIssueService.cs:262–265 | 262–265 |
| movement | GaoApp.Application/Services/Inventory/InventoryMovementService.cs:143–173 | 143–173 |
| movementNormalWrite | GaoApp.Application/Services/Inventory/InventoryMovementService.cs:462–478 | 460–476 |
| operationComplete | GaoApp.Application/Services/Invoices/AutoInvoiceService.cs:1521–1559 | 1510–1548 |
| poApprove | GaoApp.Application/Services/Purchases/PurchaseOrderService.cs:510–537 | 510–537 |
| poApproveCtrl | GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs:433–440 | 433–440 |
| poReceiptCtrl | GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs:489–503 | 489–503 |
| pos | GaoApp.Application/Services/Orders/POSService.cs:2181–2269 | 2181–2269 |
| receiptApprove | GaoApp.Application/Services/Inventory/StockDocumentService.cs:1498–1525 | 1473–1500 |
| receiptApproveCtrl | GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs:507–515 | 475–483 |
| receiptCommit | GaoApp.Application/Services/Inventory/StockDocumentService.cs:1883–1931 | 1828–1876 |
| receiptCreate | GaoApp.Application/Services/Inventory/StockDocumentService.cs:189–247 | 185–243 |
| receiptCreatedSave | GaoApp.Application/Services/Inventory/StockDocumentService.cs:351–362 | 347–358 |
| receiptMovement | GaoApp.Application/Services/Inventory/StockDocumentService.cs:1814–1826 | 1759–1771 |
| receiptRepo | GaoApp.Infrastructure/Repositories/Inventory/StockDocumentRepository.cs:632–684 | 521–573 |
| singleClaimDefinition | GaoApp.Application/Services/Invoices/AutoInvoiceService.cs:1348–1378 | 1351–1381 |
| singleClaimSave | GaoApp.Application/Services/Invoices/AutoInvoiceService.cs:1487–1496 | 1476–1485 |
| sql | GaoApp.Infrastructure/Data/SqlServerConfiguration.cs:18–28 | 18–28 |
| tripCommand | GaoApp.Infrastructure/Services/Delivery/DeliveryTripService.cs:200–221 | 200–221 |
| tripCommit | GaoApp.Infrastructure/Services/Delivery/DeliveryTripService.cs:274–280 | 274–280 |
| tripCtrl | GaoApp.Web/Areas/Admin/Controllers/DeliveryTripController.cs:27–30 | 27–30 |
| typedMovement | GaoApp.Application/Services/Inventory/InventoryMovementService.cs:1416–1439 | 1414–1437 |
| uow | GaoApp.Infrastructure/Data/AppUnitOfWork.cs:33–59 | 33–59 |
| viettel | GaoApp.Infrastructure/Services/Invoices/ViettelInvoiceIssueClient.cs:90–119 | 90–119 |
| worker | GaoApp.AutoInvoiceWorker/AutoInvoiceWorkerHostedService.cs:58–75 | 58–75 |
| workerTimer | GaoApp.AutoInvoiceWorker/AutoInvoiceWorkerHostedService.cs:20–30 | 20–30 |

## Giới hạn chưa xác minh

Chưa chạy runtime, SQL hoặc nhà cung cấp; chưa kiểm tra correctness tiền/kho, concurrency, schema production hay topology IIS/instances. Chưa khai triển toàn bộ nhánh Group/resume/unknown/correction, XML/giá cước, CSKH/Zalo, khuyến mãi/điểm thưởng và báo cáo. Không có ERD đầy đủ. Quan hệ được vẽ là lời gọi source đã lần theo, các phần chưa xác minh ghi trong cards hoặc để ngoài đồ thị. Không đưa mật khẩu, token, connection string hoặc bản ghi khách hàng thật vào artifact.

## Danh sách file

[files-created.txt](files-created.txt) liệt kê toàn bộ file G2; [manifest toàn bộ bộ sơ đồ](../files-created.txt) liệt kê G1 + G2. Root index.html và files-created.txt được cập nhật; các HTML native G1 giữ nguyên. Thời điểm tổng hợp: 2026-10-24T11:55:26.893Z.
