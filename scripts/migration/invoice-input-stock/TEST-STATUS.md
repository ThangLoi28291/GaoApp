# Kết quả rehearsal TEST — 23/09/2026

SQL TEST: PASS. Kiểm tra thực tế trên giao diện bởi người dùng: PENDING.
Chưa chốt gói để chạy dữ liệu thật.

## Cập nhật v2 — tham chiếu chứng từ

- Đã thêm schema LegacyOrderId / LegacyInvoiceNumber / LegacyInvoiceSymbol vào GaoAppDb TEST.
- DRYRUN rồi COMMIT: 0 insert, 251.427 update tham chiếu, 0 retire. 4.535 dòng tồn đầu không đổi.
- SHA256 báo cáo số dư 10.339 biến thể và 49.673 dòng biến thể/tháng giống hệt v1; lượng tồn không đổi.
- VERIFY: exact match 255.962 dòng, kế hoạch chạy lại 0/0/0.
- 38 kiểm thử InvoiceInputStock PASS; cú pháp JavaScript PASS; ReadVerifier kiểm đủ dữ liệu tham chiếu 255.962 dòng.
- Dòng ảnh: GSTORE-IIS-V1|I|937641 -> HĐ 3819, ký hiệu C26MNP, OrderID NULL, nhập lịch sử +8 đơn vị.
- Ví dụ xuất: GSTORE-IIS-V1|X|521596 -> OrderID 1799381, InvoiceNumber C26TVK270. Ký hiệu nguồn riêng đang là C26MTL: giữ nguyên nguồn, không suy diễn lại số hóa đơn.
- Chưa publish/restart ứng dụng đang chạy; thay đổi UI có hiệu lực khi chạy lại bản source đã cập nhật. Chưa kiểm tra giao diện trình duyệt.
- Evidence v2: `.artifacts/invoice-stock-migration/references-v2-*`.

## Đã thực hiện ở v1

- Nguồn TEST: .\SQLEXPRESS / DataGaoStore. Đích TEST: GaoAppDb, StoreId=1, WarehouseId=1.
- PREVIEW khớp phép tính độc lập đã thảo luận.
- DRYRUN: ghi thử 255.962 dòng, exact verification PASS, ROLLBACK. Sau rollback bảng đích vẫn 0 dòng.
- Backup COPY_ONLY và RESTORE VERIFYONLY WITH CHECKSUM: PASS trước COMMIT TEST.
- COMMIT TEST: 255.962 dòng mới; không update/retire dữ liệu sẵn có.
- POSTVERIFY và VERIFY cuối: khớp 255.962 dòng. Kế hoạch chạy lại = 0 insert / 0 update / 0 retire.
- Bài thử thay đổi nguồn trong staging: 1 insert + 1 update + 1 retire, exact verification PASS rồi ROLLBACK.
- 37 kiểm thử InvoiceInputStock: PASS. Build ReadVerifier: 0 lỗi, 0 cảnh báo.
- Repository thực tế đọc đủ 255.962 dòng, 10.339 biến thể. Service trang trả đúng số sản phẩm và số âm.
- Thời gian đo một lần: repository 2.764 ms, BuildPage 322 ms; không phải cam kết hiệu năng mọi môi trường.
- Chưa deploy source lên IIS/publish và chưa kiểm tra giao diện trong trình duyệt.

## Số liệu snapshot này

- Từ 01/06/2025 đến hết 11/07/2026; cutoff exclusive = 12/07/2026.
- 10.471 mã nguồn có lượng khác 0; 10.372 mã ánh xạ được; bỏ qua 99 mã.
- 33 mã đổi đơn vị; gộp thành 10.339 biến thể GaoApp.
- 4.535 dòng tồn đầu, 34.472 dòng nhập, 216.955 dòng xuất.
- 682 biến thể âm, 551 bằng 0, 9.106 dương.
- Bỏ qua: 78 mã có tồn đầu và 24 dòng nhập; không có dòng xuất bị bỏ do không ánh xạ.
- 13 dòng Warranty NULL đồng thời Quantity NULL/0 không tạo nhập.
- 121 header có số hóa đơn không có chi tiết: không suy ra số lượng; theo quyết định dùng đúng chứng từ còn tính được.
- Khi kiểm tra repository sau commit, không có movement runtime khác ngoài gói này.

Các số trên chỉ là evidence TEST, không dùng làm điều kiện hard-code cho REAL.

## Bước tiếp theo: kiểm tra UI

Build/chạy lại GaoApp từ source đã cập nhật; chọn đúng cửa hàng và Kho chính (WarehouseId=1).
Mở /admin/invoice-input-stock. Kỳ vọng hiện tại: 10.339 sản phẩm/kho, 682 có khả dụng âm.
Trong lượt UI đầu tiên, cột “Nhập từ XML” còn cộng cả tồn đầu và nhập lịch sử. Source đã được sửa sau phản hồi người dùng: tách cột “Tồn đầu” và “Nhập / tăng”, giữ nguyên tổng tồn và khả dụng; cần build/chạy lại ứng dụng để kiểm tra giao diện mới.

| ProductVariantId | Mã GaoApp | ĐVT gốc | Tồn đầu | Nhập | Xuất | Khả dụng |
|---|---|---|---:|---:|---:|---:|
| 155583 | 5Probitruyenthong | Chai | 181 | 1.060 | 635 | 606 |
| 159836 | 12G159836 | gr | 10.672 | 2.000 | 820 | 11.852 |
| 477197 | 0984712477197 | gr | 0 | 0 | 10.245 | -10.245 |
| 519044 | 8935212813846 | Chai | 0 | 7 | 3 | 4 |

Nhấp xem lịch sử, kiểm nguồn O/I/X, ngày UTC+7 và số trước/sau.
Lọc tháng 6/2025, tháng 12/2025, tháng 7/2026; tháng 7 chỉ có dữ liệu đến ngày 11.
Báo cáo đầy đủ trong evidence/verify-final-20260923; chọn thêm các mặt hàng quen thuộc để kiểm nghiệp vụ.
Khi dữ liệu mới phát sinh ở TEST, số UI có thể khác snapshot này; chạy PREVIEW mới để so cùng thời điểm.

## Thay đổi ứng dụng cần có

- GaoApp.Domain/Enums/InvoiceInputStockSupplementalMovementType.cs: thêm LegacyOutbound=4.
- GaoApp.Infrastructure/Repositories/Invoices/InvoiceInputStockReadRepository.cs: nhận loại 4, nhãn Xuất HĐĐT lịch sử.
- GaoApp.Application/Services/Invoices/InvoiceInputStockReadService.cs: dùng lại lịch sử từng kho/sản phẩm để tránh quét toàn bộ 255.962 dòng cho mỗi sản phẩm.
- Kiểm thử xác nhận LegacyOutbound làm giảm ledger và khả dụng phát hành, không tạo tồn vật lý.

Bản cũ không nhận loại 4 sẽ bỏ qua lượng xuất lịch sử. Cần dùng source đã cập nhật trước khi đánh giá giao diện hoặc phát hành trên dữ liệu này.
Thư mục app-source trong ZIP chỉ là bản lưu các file đã cập nhật để tham khảo/merge; không phải bản publish độc lập.
ReadVerifier cần nằm tại scripts/migration/invoice-input-stock/ReadVerifier trong repo để project references hoạt động.

## Backup TEST

C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\GaoAppDb-before-invoice-stock-test-20260923-110107.bak
RESTORE VERIFYONLY + CHECKSUM đạt; chưa thực hiện restore thử.
File backup khoảng 5,43 GB, lưu riêng, không nằm trong ZIP.

## Khi nguồn thật tăng mỗi ngày

Giữ gói SQL, lấy snapshot nguồn mới để rehearsal lại; chuẩn bị danh mục/ánh xạ tương ứng ở TEST.
Xem lại danh sách bị bỏ qua sau mỗi snapshot vì sản phẩm mới có thể chưa có đích.
Không chạy các gói reset danh mục cũ vào hệ thống đang hoạt động mà chưa xem đầy đủ phạm vi reset.
Gói tính ngày cuối động và đồng bộ theo ID nguồn; bản source mới có thêm/sửa/bỏ dòng phải PREVIEW/DRYRUN lại.
Chỉ sau khi người dùng xác nhận TEST UI đúng mới lên lịch chốt dữ liệu thật, backup, ngừng ghi/snapshot nhất quán, dry-run, commit và verify.
