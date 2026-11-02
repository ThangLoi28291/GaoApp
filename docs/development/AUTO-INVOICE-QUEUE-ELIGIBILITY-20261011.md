# AutoInvoice chọn sai bản nháp sau nửa đêm

## Hiện tượng và nguyên nhân đã xác nhận

Ngày 11/10/2026, cửa hàng 1 bật AutoInvoice, phạm vi Hôm nay, múi giờ `SE Asia Standard Time`, tuổi tối thiểu 1 phút. Cockpit hiển thị hai đơn lẻ sẵn sàng: hóa đơn 104582 (125.000 đồng) và 104583 (200.000 đồng). Heartbeat vẫn cập nhật nhưng không có operation cho hai hóa đơn này.

Đọc hàng đợi bằng DLL của Worker đang chạy tái hiện được việc chọn nhóm gồm 104580 và 104581. Cả hai là bản nháp ngày trước, tổng tiền 0 và không có dòng hàng. Khi claim nhóm, Worker bỏ qua cả hai và rollback, sau đó lại chọn cùng nhóm ở chu kỳ tiếp theo.

Truy vấn trước đây cho phép `Order.CompletedAtUtc` **hoặc** `InvoiceDate` nằm trong phạm vi UTC. `InvoiceDate` của các bản nháp cũ mang giờ địa phương nên bị lọt vào cửa sổ ngày mới sau nửa đêm. Cockpit lọc lại theo thời điểm bán nên không hiển thị chúng; Worker chưa lọc lại ngày bán và nội dung có thể phát hành trước khi chọn nhóm.

## Bản sửa

- Repository ưu tiên `Order.CompletedAtUtc`; chỉ dùng `InvoiceDate` khi không có thời điểm hoàn tất đơn. Quy tắc này cũng áp dụng khi xóa lỗi giải mã mật khẩu có thể phục hồi trong phạm vi.
- Worker kiểm tra lại cửa sổ ngày bán như cockpit và chỉ đưa vào lựa chọn các hóa đơn tổng tiền dương, có ít nhất một dòng hàng chưa xóa với số lượng và tổng tiền dương.
- Cockpit và bước claim nhóm dùng chung điều kiện về nội dung có thể phát hành.
- Chính sách chọn lượt loại giá trị 0/âm ở cả luồng đơn lẻ và nhóm. Nhóm tồn ngày trước hợp lệ vẫn chạy khi nằm trong phạm vi tháng/khoảng ngày đã cấu hình.

Không thay đổi dữ liệu hóa đơn, mật khẩu, menu hay cấu hình cửa hàng để sửa lỗi này.

## Kiểm chứng

- Build và chạy kiểm thử trong thư mục riêng, tránh ghi đè DLL đang được Visual Studio sử dụng.
- 115 kiểm thử `GaoApp.Tests.Invoices.AutoInvoice*` đạt, gồm các ca hồi quy: hai bản nháp cũ 0 đồng không chặn hai đơn mới; phạm vi ngày UTC+7 sau nửa đêm; biên đầu/cuối ngày; fallback khi thiếu `CompletedAtUtc`; bản nháp không có dòng hàng hợp lệ; nhóm tồn ngày trước hợp lệ trong phạm vi tháng.
- Probe chỉ đọc SQL bằng bản sửa, với tenant Worker (null) và tenant Web (1), đều trả đúng 104582 và 104583; lựa chọn đầu tiên là đơn lẻ 104582. Hai hóa đơn đều qua kiểm tra route/cấu hình phát hành.
- Không gọi API phát hành Viettel trong quá trình chẩn đoán/kiểm thử. Cần chạy lại Worker bằng bản build mới để xác nhận phát hành thực tế.

## Chạy lại trên máy phát triển

1. Trong Visual Studio, chọn **Debug → Stop Debugging** (`Shift+F5`) để dừng Web và Worker đang chạy bằng bản cũ.
2. Chọn **Build → Rebuild Solution**.
3. Chạy lại Web và `GaoApp.AutoInvoiceWorker` bằng cấu hình chạy Worker bình thường (không chọn cấu hình chỉ kiểm tra key/mật khẩu).
4. Tải lại `/Admin/AutoInvoice?tab=single`, theo dõi các operation của 104582 rồi 104583 và tab Đã Phát Hành. Mỗi chu kỳ chỉ chọn một operation; cấu hình hiện tại có khoảng gửi 20 giây.

Chỉ kết luận phát hành thành công sau khi có kết quả provider/operation thực tế; kết nối cấu hình và heartbeat không thay thế xác nhận này.
