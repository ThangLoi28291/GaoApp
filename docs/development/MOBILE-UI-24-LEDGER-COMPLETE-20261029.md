# UI-24 — hoàn tất source và nghiệm thu cục bộ, bổ sung thẻ kho (29/10/2026)

> Cập nhật tiếp theo: giới hạn tìm “Tất cả” đã được tối ưu và nghiệm thu cục bộ; xem [báo cáo mới](INVENTORY-LEDGER-ALL-SEARCH-20261029.md). Số liệu bên dưới là bản ghi đợt UI-24 trước.

Đã hoàn tất source và các kiểm tra cục bộ trong phạm vi đã thống nhất. Người dùng xác nhận **chỉ làm source/kiểm thử cục bộ, nghiệm thu host và thiết bị thật để sau**. Giữ nguyên giao diện máy tính, quyền/cấu hình cửa hàng và các cửa sổ Chrome đang mở. **Không làm phần ngày hết hạn voucher**.

## Thẻ kho đã bổ sung

- Trên điện thoại, thêm khối **Tổng đầu / cuối kỳ**, chọn từng sản phẩm tại từng kho từ giao dịch. Tính bằng đơn vị gốc, không cộng gộp sản phẩm hoặc đơn vị khác nhau.
- Tồn đầu kỳ = số trước lần ghi sổ đầu tiên + tất cả phát sinh trước ngày bắt đầu. Tồn cuối kỳ = đầu kỳ + nhập/tăng − xuất/giảm. Khoảng ngày theo giờ Việt Nam, từ đầu ngày đến trước đầu ngày kế tiếp; có tính giao dịch ghi muộn/ghi lùi ngày. Không lấy BeforeQty/AfterQty của dòng đang nhìn thấy để suy ra số tồn theo ngày nghiệp vụ.
- Tổng kỳ đọc toàn bộ lịch sử của đúng sản phẩm/kho, độc lập phân trang, thứ tự, loại giao dịch, chứng từ, trạng thái và từ khóa ghi chú. Chọn sản phẩm được ghi rõ trên khối tổng. Đổi sang kết quả khác sẽ theo sản phẩm mới; kỳ không phát sinh giữ sản phẩm đã chọn và vẫn có đầu/cuối kỳ. Không có lịch sử thì báo chưa xác định, không suy đoán từ tồn hiện tại.
- Thêm **Mở chứng từ nguồn** trên thẻ/chi tiết điện thoại. Dùng ReferenceType và định danh đã đối chiếu với bản ghi cùng cửa hàng: nhập kho, nhập từ đơn mua, đơn bán, hoàn trả (mở đơn bán gốc), kiểm kê, chuyển kho, điều chỉnh, phiếu giao hàng. Không dựng URL từ mã hiển thị/ghi chú. Nguồn cũ không xác định hoặc không đủ quyền sẽ không có liên kết.
- Phiếu nhập kho và phiếu nhập từ đơn mua dùng đúng hai nhóm quyền hiện có. Mỗi lần mở nguồn kiểm tra lại quyền và sự tồn tại của chứng từ. Endpoint mới đều yêu cầu Inventory.Transaction.View và no-store; không phát sinh ghi kho, thay giá vốn, menu hay cấu hình.

Đường dẫn hiện có: /admin/inventory-ledger. API đọc thêm: /admin/inventory-ledger/period và /admin/inventory-ledger/source. Không tạo menu tự động; không cần migration mới.

## Kiểm tra hoàn thành

| Phạm vi | Kết quả |
|---|---|
| Build và schema | Build thành công, 0 lỗi; 1 cảnh báo xUnit2031 có sẵn. MigrateAsync trên SQL GUID; không pending model/migration; 143 file migration cũ giữ nguyên. |
| Hồi quy đợt này | **619/619 ca .NET riêng biệt**, **721/721 Node**, **23/23 kiểm tra an toàn deployment** (mock, không deploy IIS). Không cộng lặp các lần chạy cùng một ca. |
| Thẻ kho SQL | Đúng mốc giờ Việt Nam, timestamp trùng nhau, giao dịch lùi ngày, kỳ trống, khoảng ngày đảo, kho riêng có baseline cũ, kho chưa ghi sổ, phân trang và bộ lọc. Chặn nguồn khác cửa hàng, nguồn giả/legacy và thiếu quyền; kiểm tra lại sau đổi nguồn phiếu nhập và xóa chứng từ. |
| Trình duyệt thật | Chrome headless riêng: 320/390/430 px không tràn ngang; giữ form khi resize; tổng kỳ lỗi xóa số cũ và có thử lại; xem thẻ kho không mặc nhiên được mở nguồn. Desktop không gọi API tổng kỳ; khối mới ẩn có đối chiếu pixel. Luồng nhập kho thật, đơn vị/barcode và giá vốn ADMIN tiếp tục đạt. |
| Dữ liệu lớn | **1.500.047 phát sinh**, tổng kỳ **1229 ms**, kết quả đúng. Tìm sản phẩm/SKU/barcode và phân trang giữ đúng dữ liệu. |
| Đóng gói | **283 Razor, 441 asset riêng**, 2075 source C# production được compile; asset/gzip khớp source. Gói WebOnly, không Migrator, không seed. |
| HTTPS/khôi phục | Production HTTPS cục bộ: baseline → bản mới → bản giải nén phục hồi, cùng SQL/keys/cookie. Cookie tiếp tục hợp lệ, barcode mới còn sau rollback, menu/quyền/cửa hàng/đơn/tiền không đổi. API tổng kỳ từ DLL đã publish được kiểm tra. 54 mẫu đo trên cùng fixture; cả 6 nhóm route/viewport nằm trong budget xem xét. |

Các kiểm tra source khẳng định 39 file voucher/RewardSettings không đổi. Snapshot SQL trước/sau các lượt đọc cũng giữ nguyên kho, ledger, menu, quyền, cửa hàng và cấu hình/voucher. Hai lần thử đầu của fixture mới dừng vì QuotedTotal=0 và ReferenceId=null; đã sửa dữ liệu fixture theo schema, không nới ràng buộc. Các TRX thất bại được giữ lại; chỉ tính kết quả cuối.

Ma trận UI-24 trước (253 trạng thái, 1771 phone, 506 desktop, 234 print; 24 PDF/48 PNG) và 51 verification lịch sử được giữ nguyên. Đợt bổ sung này chạy lại 613 ca core, 2 ca ledger dữ liệu lớn/tìm nguồn, 3 ca SQL/native ledger/nhập kho/giá vốn, 1 ca release và 721 Node; các probe native mô-đun khác của đợt UI-24 trước không bị cộng thành lượt chạy mới.

## Giới hạn và phần để sau

**Tìm “Tất cả” không giới hạn ngày vẫn có truy vấn 14–34 giây trên 1,5 triệu dòng**, có thể vượt timeout 20 giây của điện thoại. Đây là giới hạn cũ của truy vấn quét chữ tự do; đợt này không đổi schema/tìm kiếm. Tổng kỳ mới đạt 1229 ms. Chưa chứng nhận tải nhiều người dùng, WAN/4G hoặc host thật.

Android/iPhone, máy in/QZ/helper/LabelPrintClient, máy quét, két và rollout/theo dõi trên host thật được **hoãn theo yêu cầu của người dùng**, không ghi là đã test. Phần hết hạn voucher cũng hoãn, không đổi DTO hay giao diện voucher. UI-24f được giữ làm bước nghiệm thu môi trường thật khi có điều kiện.

Gói phát hành: E:\GaoApp\GAOAPP-SA-20260819-01\publish\releases\20261029-033525-0bc7e230

Manifest SHA-256: D0FEF031DF24E2AF230DAD5DB2D8AD31372462F75B1ED56D12AF752892B99941

Theo quy trình TH1 trong [runbook](GAOAPP-DEPLOYMENT-RUNBOOK-20260913.md) khi triển khai thật: WebOnly, không Migrator/seed. Phép rollback cục bộ giữ nguyên schema/dữ liệu; bản cũ sẽ không có phần tổng kỳ/liên kết mới.

[Bằng chứng tổng](mobile-ui-20261025/evidence/ui-24-ledger/runtime-review.json), [source](mobile-ui-20261025/evidence/ui-24-ledger/source-review.json), [kiểm kê](mobile-ui-20261025/evidence/ui-24-ledger/inventory-review.json), [build/package](mobile-ui-20261025/evidence/ui-24-ledger/build-review.json), [hiệu năng](mobile-ui-20261025/evidence/ui-24-ledger/performance-review.json), [ảnh thẻ kho với nhập/xuất thật](../../TestResults/mobile-ui-24-ledger-20261029/ui13-regression/lookup/ledger-admin-movements-phone.png), [bản ghi UI-24 trước](MOBILE-UI-24-ACCEPTANCE-20261029.md).
