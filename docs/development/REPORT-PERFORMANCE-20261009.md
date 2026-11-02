# Tối ưu Tổng quan kinh doanh — 09/10/2026

## Triệu chứng và phạm vi xác minh

Trang `/admin/reports/overview` trên server cuối cùng tải được, nhưng chậm khi có nhiều dữ liệu. File JavaScript công khai của server `chonthanh.gaomart.com.vn:8443` khớp bản local trước sửa. API báo cáo yêu cầu đăng nhập; chưa đo thời gian SQL, tải mạng hay tổng thời gian trang trên server thực tế.

Đo bằng truy vấn chỉ đọc trên `GaoAppDb` local, có 1.535.364 dòng giá vốn. Không chạy migration, sửa dữ liệu kinh doanh hoặc phát hành hóa đơn trong quá trình đo.

## Các điểm đã sửa

- Truy vấn giá vốn trước đây `UNION` toàn bộ cột của hai nhánh. Chuyển sang `UNION` ID rồi đọc mỗi dòng một lần. Vẫn giữ nhánh liên kết qua giao dịch để phát hiện chứng cứ có mã đơn sai; không lọc bỏ dữ liệu bất thường.
- Ghép chứng cứ ngân sách giá vốn bằng lookup thay cho quét toàn bộ danh sách debit ở mỗi entry. Allocation vẫn phải thuộc đúng entry; liên kết hỏng không được tự sửa.
- Trang Tổng quan gọi API với `view=overview`: tính trên toàn bộ snapshot, trả tối đa 6 sản phẩm có doanh thu và 5 khách hàng có tên, cùng toàn bộ KPI, xu hướng và tổng nhóm khách hàng. Không gửi danh sách chiều chi tiết không hiển thị, đặc biệt của kỳ so sánh. API không có `view` và CSV vẫn đầy đủ; không sửa DTO nguồn.
- Thời gian chờ phía trình duyệt bằng giới hạn xử lý + hàng đợi của server + 10 giây. Khi quá hạn hoặc phản hồi lỗi, kết thúc trạng thái tải và cho thử lại. Yêu cầu cũ không ghi đè yêu cầu mới. Lỗi HTTP có mã tra cứu; HTML thay cho JSON được phát hiện.

Giữ nguyên transaction snapshot, giới hạn nguồn dữ liệu, phân quyền và tenant, cách xác định giá vốn cuối cùng/tạm tính, trả hàng, hủy đơn, chi phí và số liệu xuất CSV. Không cache kết quả tài chính.

## Kết quả đo local

Mỗi ngày chạy cũ/mới xen kẽ 4 vòng, đảo thứ tự ở vòng lẻ; bỏ vòng đầu có khởi tạo EF/JIT. Bảng thời gian là trung vị 3 vòng đã khởi động, tính toàn bộ service; chưa tính serialize và truyền mạng. Dữ liệu nguồn gồm cả kỳ so sánh.

| Ngày | Đơn bán kỳ hiện tại | Entry nguồn | Service cũ → mới | SQL cũ → mới | JSON đầy đủ → Tổng quan |
|---|---:|---:|---:|---:|---:|
| 09/10/2026 | 4 | 12 | 18 → 19 ms | 8 → 8 ms | 41.961 → 31.105 byte |
| 23/06/2026 | 300 | 2.397 | 437 → 391 ms | 181 → 169 ms | 1.235.966 → 40.219 byte |
| 14/02/2026 | 911 | 7.932 | 886 → 800 ms | 543 → 470 ms | 2.834.848 → 41.427 byte |

Dung lượng là JSON UTF-8 chưa nén; mức giảm ngày lớn là 98,54%. Thời gian SQL đo bằng interceptor đến lúc nhận reader, không bao gồm toàn bộ quá trình đọc từng hàng. Không suy ra tốc độ server thực tế từ số đo local.

Đối chiếu chính xác chuỗi JSON đầy đủ của mọi vòng (giữ nguyên số thập phân, chỉ bỏ timestamp tạo kết quả): tất cả chỉ tiêu, chiều dữ liệu và thứ tự dòng giống hệt trước/sau.

## Kiểm chứng và triển khai

Build Release của Web/Tests thành công, không lỗi hoặc warning. Tổng cộng 111 ca đã qua: lần đầu 109 ca qua, 2 ca có lỗi trong fixture mới; sau khi sửa fixture, chạy lại cả hai ca thành công (`reports-performance-repaired.trx`). Không có lỗi sản phẩm còn lại từ bộ kiểm thử này. Kiểm thử báo cáo bao phủ số liệu tài chính, provenance lỗi, missing cost, provisional cost, trả hàng/hủy đơn, tenant, quyền, snapshot khi có ghi đồng thời, budget và export. Browser fixture riêng kiểm tra phản hồi Tổng quan, trang sản phẩm đầy đủ, timeout/thử lại, lỗi HTTP/trace, phản hồi HTML và yêu cầu cũ.

Bằng chứng local nằm trong `Logs/reports-server-load-20261009`: `comparison-summary.json`, `comparison.log`, JSON đối chứng, build logs, test TRX và ảnh browser. Các JSON đầy đủ có dữ liệu local; không đăng công khai.

Chưa triển khai server. Khi triển khai cần cập nhật cả backend, view và `wwwroot/Admin/js/reports/management-report.page.js`; thẻ script đã có `asp-append-version`. Không có migration mới cho thay đổi này. Sau triển khai kiểm tra request `/admin/reports/management/data?...&view=overview` trong Network: dung lượng phản hồi, thời gian chờ server và tải nội dung, rồi đối chiếu cùng bộ lọc với trang sản phẩm/xuất CSV.
