# Báo cáo tổng quan và giá vốn đơn giao hàng — 13/10/2026

## Xác minh trên hệ thống thật

- Trang `/admin/reports/overview`, ngày 12/10/2026: 205 đơn bán, doanh thu thuần 27.570.681 đồng.
- Chi tiết theo giờ khoanh cảnh báo vào 16:00; đơn `POS-20261012-00118`, ID 1837627, lúc 16:02, doanh thu 1.000.001 đồng.
- Đơn là hóa đơn chốt theo hàng thực giao của `GH-20261012-4526B66F56636BB1`, hồ sơ giao #1; 240 chai Aquafina 500ml, đã chốt và thanh toán.
- Báo cáo cũ chỉ tìm outbound `ReferenceType.Order` theo ID/dòng bán. Luồng bàn giao giao hàng tạo hóa đơn thanh toán và allocation trỏ về xuất giao gốc, chủ động không xuất kho POS lần hai. Vì vậy báo cáo không tìm thấy outbound POS mặc dù chứng cứ xuất giao tồn tại.
- Chỉ đọc các trang thật; không phát hành hóa đơn, chốt lại đơn, sửa tiền, tồn kho hay dữ liệu giá vốn.

## Thay đổi

- `DeliveryProfitCostPolicy` đối chiếu chứng cứ `delivery-handover-v1`, hash nội dung, journal thanh toán, hồ sơ giao, thời điểm/tiền chốt, dòng hàng và allocation với các lát giá vốn xuất giao gốc. Nguồn ngân sách kiểm tra debit, layer, khoảng số lượng và phép chia tiền chính xác. Nguồn âm còn tạm tính giữ đúng trạng thái; nguồn Unknown không trở thành giá vốn 0.
- Repository đọc chứng cứ giao theo lô trong cùng transaction Snapshot và giới hạn số dòng hiện có. Không gọi dịch vụ promote nguồn, không sửa ledger hoặc tạo outbound giả.
- Lịch sử điều chỉnh nguồn giao chưa được phản ánh vào chứng cứ bàn giao sẽ hiện nguyên nhân cần đối chiếu; không dùng âm thầm giá cũ. Luồng hoàn/hủy POS thông thường của hóa đơn giao vẫn không được coi là lịch sử hoàn giá vốn hợp lệ.
- Báo cáo có chẩn đoán riêng: số đơn/dòng bị ảnh hưởng, thiếu dữ liệu nguồn hay dữ liệu không khớp; tối đa 20 nguyên nhân kèm mã đơn, sản phẩm. Link chi tiết giữ bộ lọc ngày/quầy/khách, chỉ xem `segment=issues`. Link mở đơn chỉ hiện khi có quyền xem đơn.
- Các cảnh báo được xây từ đúng sự kiện doanh thu hợp lệ trong kỳ; không lộ đơn ngoài bộ lọc, không đếm trùng đơn do có sự kiện trả hàng.
- Đọc bản chiếu nhỏ của đơn/dòng bán và chứng từ trả thay cho toàn bộ ghi chú, snapshot khuyến mãi và trường không dùng.
- `ReadOverviewAsync` giữ nguyên toàn bộ số tiền, chất lượng, biểu đồ và nhóm khách; chỉ tính giá vốn chi tiết cho 6 sản phẩm/5 khách được hiển thị, bỏ bảng nhóm sản phẩm và chi tiết không dùng của kỳ so sánh. API đầy đủ và CSV tiếp tục trả mọi dòng.
- Không thêm menu, migration/index hoặc cache kết quả tài chính.

## Hiệu năng cục bộ

Đo luân phiên bản repository gốc của bộ đo trước với bản tổng quan mới, ngày 14/02/2026, 911 đơn bán; source cả hai giữ 1.770 đơn của hai kỳ, 7.802 dòng và 7.932 bút toán. Bỏ vòng khởi động, trung vị 3 vòng:

| Chỉ số | Bản đọc gốc + báo cáo đầy đủ | Tổng quan tối ưu |
| --- | ---: | ---: |
| Xử lý | 1.584 ms | 1.402 ms |
| Đọc source | 1.452 ms | 1.299 ms |
| Tính toán | 159 ms | 109 ms |
| JSON đầy đủ / tổng quan | 2.844.483 byte | 49.406 byte |

Đây là phép đo SQL cục bộ, không phải tốc độ server thật sau triển khai. API overview trước đó đã giảm JSON sau tính toán; lần này giảm cả công việc tính các bảng không hiển thị. Log: `TestResults/report-overview-comparison-20261013.log`. Các file JSON source chứa dữ liệu nội bộ, không dùng làm tài liệu công khai.

## Xác minh và triển khai

- Kiểm thử native giao/chốt/nhận trả trên cơ sở dữ liệu kiểm thử riêng: giao đầy đủ, giao một phần thập phân, nguồn opening, nguồn âm tạm tính và nguồn Unknown. So sánh tổng quan với API đầy đủ và lọc chi tiết lỗi; xác nhận snapshot tiền/kho không đổi sau đọc báo cáo.
- Thử hỏng chứng cứ detached: hash, giá vốn fragment, allocation kho, sai cửa hàng; báo cáo phải giữ giá vốn rỗng và nêu nguyên nhân.
- Kiểm thử phạm vi/đếm nguyên nhân, nguồn POS cũ, chất lượng giá vốn, quyền truy cập, CSV và giao diện tải lại/lỗi/timeout/phản hồi cũ.
- Bộ kiểm thử báo cáo/SQL/giao diện: 140 kiểm thử đạt, 0 lỗi. Log hoàn tất: `TestResults/report-complete-upgrade-20261013.log`.
- Kiểm thử cuối cho tính tương đương của `ReadOverviewAsync` và nguồn giao điều chỉnh sau bàn giao: 14 kiểm thử đạt, 0 lỗi, biên dịch không cảnh báo; log `TestResults/report-final-validation-20261014.log`.
- Bản sửa nằm trong mã nguồn cục bộ. Cần triển khai Web/backend, Razor và `wwwroot` cùng bản để hệ thống thật nhận thay đổi; không chạy script sửa lịch sử tiền/kho.
