# Kết quả kiểm thử bước 01–03 ngày 23/09/2026

**Đã kiểm thử trong database riêng `GaoAppMigrationReview_20260923_v2`. Chưa chạy dữ liệu thật.** Nguồn đọc là `DataGaoStore`; TEST tham chiếu là `GaoAppDb`. Không ghi/xóa hai database này. Cấu hình kết nối ứng dụng không đổi.

| Nội dung | Kết quả |
|---|---:|
| Sản phẩm chính từ gói 01 | 31.366 |
| Mã hàng lịch sử bổ sung, không hoạt động | 767 |
| Mã điều chỉnh tài chính, không hoạt động | 1 |
| Khách hàng | 12.651 |
| Đơn bán | 389,661 |
| Dòng bán (gồm 847 dòng điều chỉnh tài chính) | 1,399,591 |
| Khoản thanh toán bán hàng | 385,940 |
| Ca lịch sử, đều đóng | 8,097 |
| Thu/chi ca | 7,078 |
| Trả hàng có liên kết | 117 |
| Chi tiết trả có liên kết | 274 |
| Hoàn tiền của trả có liên kết | 117 |
| Phiếu trả thiếu liên kết, lưu tra cứu | 3,568 |
| Liên kết chi tiết nguyên bản trong archive | 8.825 |

Tổng tiền bán/thanh toán: **80.349.738.864**. Tổng thu/chi nguồn được chọn: **19.119.349.205**. Tổng chi tiết trả có liên kết **18.481.400**, tổng hoàn theo đầu phiếu **18.481.390**: giữ chênh lệch 10 của nguồn như TEST, không ép sửa số cũ.

## Đã kiểm tra

- DRYRUN toàn bộ bước 03 ghi trong transaction, đối chiếu các cột đã stage, rồi rollback thành công. Sau đó điều chỉnh riêng cách ghi giờ/ghi chú thu chi: đã kiểm thử thay đổi này trong transaction, đối chiếu cả 8 bảng với staging bản cuối và thu/chi với TEST, rồi xác nhận rollback trả lại nguyên bản từng dòng. COMMIT cuối chạy lại toàn bộ gói từ các bảng rỗng.
- COMMIT bước 01, 02, 03 và archive trên rehearsal thành công; VERIFY SHA-256 nguồn/đích đạt.
- Chạy lại COMMIT bước 03 và archive trả `ALREADY_IMPORTED_NO_CHANGE`, không nhân đôi dữ liệu.
- Gói SQL đã đổi bị chặn khi thử chạy lại trên rehearsal cũ; không ghi đè.
- 10 nhóm đối chiếu nghiệp vụ với TEST đều có 0 chênh lệch: đơn, dòng bán, thanh toán, ca, thu/chi, trả hàng, hoàn tiền và thời gian. ID tự sinh của dòng điều chỉnh/child/conversion không phải khóa nguồn nên không dùng để so ngang. Ngày ca giữ độ chính xác SQL; đối chiếu thời gian cho phép đến 1 ms so với TEST cũ đã làm tròn.
- Fingerprint các bảng nguồn dùng chung không thay đổi giữa các lần chuyển 01–03/archive trên snapshot kiểm thử này.
- Archive đối chiếu nguyên bản JSON header/chi tiết, gồm đầy đủ NULL và liên kết nguồn.
- 11 kiểm tra controller archive đạt: đọc, tìm mã phiếu, phân trang, input, quyền và cô lập cửa hàng. Build Web/verifier không warning/error. Chưa kiểm tra trực quan giao diện trên trình duyệt đang chạy.

## Các ngoại lệ được giữ rõ

- Không chuyển công nợ cũ theo quyết định của người dùng; đơn lịch sử giữ Paid/BalanceDue=0 như TEST.
- 12 đầu đơn âm không nạp và có báo cáo riêng. 11 đơn dùng khách mặc định legacy ID 8 khi không tìm thấy khách đã chuyển.
- 236 ca có số cuối tính gốc âm: giữ số gốc trong ghi chú/báo cáo, trường cuối ca ghi 0 như TEST.
- 27 phiếu thu/chi thuộc ca không có đơn bán/trả và thiếu ngày chốt: dùng giờ mở ca, ghi rõ ngày nguồn trong note như TEST. Các phiếu thu/chi còn lại giữ ngày nguồn; cột NgayThang cũ chỉ lưu DATE.
- Tên hàng lịch sử lấy từ bảng Product nguồn khi chỉ có một tên xác định; trường hợp nhiều tên giữ nhãn theo mã.
- Số lượng trong báo cáo này chỉ mô tả snapshot TEST, không hard-code thành điều kiện số dòng cho dữ liệu thật.

## Phạm vi chưa hoàn tất

Gói này chưa chứa công cụ dọn dữ liệu đích, chưa xác nhận toàn chuỗi 04 kho/giá vốn → 05 tồn hóa đơn → 06 hóa đơn với bước 03 mới. `TABLE-PLAN.md` là kế hoạch phân loại bảng, không phải lệnh dọn. Cần hoàn tất và diễn tập toàn chuỗi trên snapshot mới trước khi chốt chạy thật.

Bằng chứng chi tiết tại `.artifacts/migration-review/v2/` trong workspace. ZIP chỉ chứa source, hướng dẫn và báo cáo tổng hợp; không chứa database, backup hay dữ liệu khách hàng nguyên bản.
