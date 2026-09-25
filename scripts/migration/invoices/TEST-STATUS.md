# Trạng thái TEST — 23/09/2026

Đã chép vào `.\SQLEXPRESS / GaoAppDb`, nguồn `DataGaoStore`. Chưa chạy dữ liệu thật; kiểm tra UI bởi người dùng còn chờ.

- 104.427 InvoiceHeads: 66.055 có số phát hành, 38.372 chưa có số.
- 337.169 InvoiceDetails, ánh xạ được mọi mã; giữ nguyên đơn vị/số lượng/tiền trên chứng từ.
- 5 InvoiceHeads / 7 InvoiceDetails sẵn có được giữ nguyên; tổng sau chép: 104.432 heads / 337.176 details.
- 108 hóa đơn điều chỉnh chỉ tra cứu theo quyết định của người dùng.
- 114 hóa đơn không liên kết được Order GaoApp: 108 điều chỉnh + 6 hóa đơn khác; vẫn có đủ đầu/chi tiết, giữ OrderID cũ, khóa tra cứu.
- 3 hóa đơn nhóm cũ “đã phát hành” nhưng không có số: giữ nguồn, khóa tra cứu để tránh phát hành nhầm. Tổng LegacyReadOnly=117; độc lập với các hóa đơn có số vốn đã khóa.
- Giữ nguyên 42 chênh lệch tổng đầu/chi tiết. 2.353 tổng đầu NULL đều không có chi tiết, tổng hiển thị mới=0; nguyên bản NULL được lưu.
- 7 đơn giá NULL được điền Amount/Quantity (cả 7 Quantity=1, thuộc nhóm điều chỉnh), giữ nguyên số tiền và raw NULL.
- Nguồn có 3 SellerCode; TEST đang dùng chung LegalEntityId=1 theo gói tồn trước, giữ SellerCode trên từng hóa đơn. Chưa chốt ánh xạ pháp nhân cho dữ liệu thật.
- Chưa có cấu hình provider TEST nào khớp nguồn; không tự chọn cấu hình demo. Chưa gọi API phát hành, email, sync hoặc tải file.

## Bằng chứng đã đạt

- Backup COPY_ONLY/CHECKSUM + RESTORE VERIFYONLY trước schema/data import: PASS. Đường dẫn trong `backup.json`; backup không nằm trong ZIP.
- Schema chạy thử và rollback: PASS; sau đó đã áp dụng migration 20260923160000_AddLegacyInvoiceImport.
- PREVIEW → DRYRUN/ROLLBACK → COMMIT: exact match toàn bộ 104.427 heads / 337.169 details.
- VERIFY cuối: exact match toàn bộ đầu/chi tiết, kế hoạch insert/update đều 0. Order/OrderLine giữ nguyên 389.667 / 1.399.600.
- Bổ sung khóa 3 dòng mâu thuẫn trạng thái: DRYRUN/ROLLBACK rồi COMMIT, chỉ update 3 đầu hóa đơn.
- 39 kiểm thử repository/nghiệp vụ/chứng từ: PASS; 18 kiểm thử migration SQL và tồn: PASS.
- Lượt mở rộng 398 kiểm thử invoice/legal entity: 394 đạt ban đầu; 4 lỗi index trong migration Down đã sửa và nằm trong 18 bài SQL chạy lại đạt.
- Bài thử nguồn thêm 1 head/1 detail, sửa 1 head: exact match rồi rollback PASS. Bài thử người dùng sửa đích: dừng ghi đè, rollback PASS.
- Build GaoApp.Web cuối: 0 lỗi, 0 cảnh báo.
- Verifier qua service danh sách/chi tiết thật: đọc đủ 104.432 hóa đơn, tìm được OrderID cũ chưa có Order mới, giữ đúng đơn vị của dòng quy đổi.
- Tồn: 255.962 movements / 10.339 biến thể / 682 biến thể âm giữ nguyên. Không phát sinh dòng trừ tồn runtime trùng từ hóa đơn vừa chuyển.
- 216.955 dòng xuất lịch sử đã nối được tới InvoiceHead mới để mở chứng từ.

Evidence: `.artifacts/invoice-migration/`. Các báo cáo PREVIEW/DRYRUN/COMMIT ghi SHA256 SQL và ngày giờ chạy. Kết quả chạy lại cuối và thử tăng/sửa nguồn được lưu riêng tại đây.

Source đã sửa nhưng chưa publish/restart ứng dụng đang chạy. Chạy lại GaoApp từ source mới rồi kiểm tra `/Admin/Invoice`: hóa đơn đã phát hành, bản nháp thường, nhóm điều chỉnh chỉ tra cứu, hóa đơn gộp, trường hợp chưa có Order, dòng quy đổi và mở chứng từ từ sổ tồn.
