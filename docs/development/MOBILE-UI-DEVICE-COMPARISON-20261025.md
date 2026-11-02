# Bổ sung bản so sánh thiết bị và menu điện thoại

Ngày 25/10/2026. Theo yêu cầu xem bản máy tính và dùng thanh menu dưới cùng như ảnh người dùng gửi, đã bổ sung trang so sánh vào bộ nền UI-01.

**Cập nhật:** trang Compare hiện mặc định mở [điện thoại v2](MOBILE-UI-MOBILE-V2-20261025.md) với năm màn riêng. Nội dung, ảnh và hash bên dưới mô tả bản so sánh v1 để lưu lịch sử; desktop vẫn là bản tham chiếu đó.

## Mở bản xem thử

- Máy chủ mẫu: `http://127.0.0.1:5206/admin/ui-foundation/compare`.
- Route trong ứng dụng: `/admin/ui-foundation/compare`, cùng controller và quyền `security.role.permissions` của trang nền.
- Ba chế độ: **Máy tính**, **Điện thoại**, **So sánh**. Khung máy tính có viewport 1440 × 960; khung điện thoại 390 × 844. Khung được thu theo vùng xem, giữ đúng breakpoint của từng thiết bị. Mỗi khung có thể cuộn và thao tác riêng.
- Trang trực tiếp `/admin/ui-foundation` tiếp tục tự thích ứng với chiều rộng thực tế.

## Thanh menu điện thoại theo mẫu đã chọn

Thanh cố định ở đáy, biểu tượng phía trên nhãn, vạch xanh trên mục đang chọn; tối đa bốn mục ghim và một nút Menu. Có safe area và khoảng trống cuối nội dung; thông báo nổi được đặt cao hơn thanh menu. Thanh chỉ xuất hiện khi chiều rộng dưới 768 px. Nhãn dài giới hạn hai dòng và có tên truy cập đầy đủ.

Trong fixture xem thử:

| Mục | Tác dụng trong demo |
|---|---|
| Tổng quan | Về phần đầu nội dung |
| Đơn hàng | Đến danh sách và chọn trạng thái điều hướng tương ứng |
| Nhập hàng | Mở sheet nhận hàng mẫu |
| Bán hàng | Mở giỏ hàng mẫu, tổng 640.000 ₫ |
| Menu | Mở bảng chọn các khu vực mẫu; chuyển tiếp sang tác vụ sau khi đóng bảng Menu |

Partial `Commerce/_BottomNavigation.cshtml` nhận danh sách item từ model. Các nhãn/điểm mở cụ thể ở trên thuộc fixture của `UiFoundation/Index.cshtml`. Layout/sidebar thật, `AdminMenuItems`, seeder, migration, dữ liệu menu và bảng ẩn/hiện menu giữ nguyên. Khi triển khai UI-02, caller phải truyền các mục từ cấu hình cửa hàng sau khi kiểm tra trạng thái, quyền và lựa chọn ghim. Việc ghim và quyền truy cập tiếp tục là hai phần độc lập.

## Kiểm tra

| Kiểm tra | Kết quả |
|---|---|
| Build preview | Qua, 0 warning/0 error; compile Razor/model/controller thật đã link |
| Route | Hợp đồng Index và Compare có HTTP GET; kế thừa đăng nhập và policy hiện có |
| 320/390 px | Thanh menu ở đáy 844 px; năm mục có vùng chạm ít nhất 59 × 72 px; không tràn ngang |
| 768/1440 px | Thanh menu dưới ẩn; bố cục tablet/desktop giữ nguyên |
| Chọn mục | Tổng quan/Đơn hàng, Nhập hàng, Bán hàng và Menu đều thao tác được |
| Chuyển Menu → Nhập hàng | Chỉ có một dialog mở; scroll lock và focus khôi phục đúng |
| Escape/đóng sheet | Trở về mục nội dung trước khi mở, giữ điểm mở để dùng tiếp |
| Nội dung cuối | Footer kết thúc ở 747,77 px; thanh menu bắt đầu ở 771 px trên viewport 390 × 844 |
| So sánh | Ba chế độ hiển thị đúng; bộ lọc ở khung máy tính không đổi khung điện thoại |
| DOM | Không trùng ID trong từng document; các iframe dùng document riêng |
| Baseline | 560 hash Razor/asset trước triển khai giữ nguyên |

Ảnh mới: [bản máy tính](mobile-ui-20261025/evidence/ui-01-navigation/desktop-1440.jpg), [điện thoại với menu](mobile-ui-20261025/evidence/ui-01-navigation/phone-orders-390.jpg), [Menu đang mở](mobile-ui-20261025/evidence/ui-01-navigation/phone-menu-390.jpg), [hai khung so sánh](mobile-ui-20261025/evidence/ui-01-navigation/comparison-1440.jpg).

Dữ liệu đo và hash: [viewport matrix](mobile-ui-20261025/evidence/ui-01-navigation/viewport-matrix.json), [kiểm tra chế độ và khung](mobile-ui-20261025/evidence/ui-01-navigation/comparison-checks.json), [kết quả bổ sung](mobile-ui-20261025/evidence/ui-01-navigation/result.json).

Phiên tự động có log `MutationObserver` không kèm URL khi ẩn/hiện iframe. Hook `window.error` tạm trên các script của trang không nhận lỗi; source các script tải trong trang không dùng MutationObserver. Có dấu hiệu liên quan lớp quan sát iframe của trình duyệt tự động, nhưng chưa có stack để xác nhận nguồn. Log được giữ nguyên trong [console observation](mobile-ui-20261025/evidence/ui-01-navigation/console-observation.json); không ghi nhận phiên này là console sạch.

Giới hạn môi trường .NET và kiểm tra thiết bị thật giữ như [bàn giao UI-01](MOBILE-UI-01-FOUNDATION-20261025.md). Chưa triển khai menu thật của UI-02 hoặc chuyển các màn nghiệp vụ. Tổng phần bổ sung hiện có so với baseline: 7 Razor, 6 CSS/JS, 1 model và 1 controller; baseline JSON và ảnh lần bàn giao trước được giữ làm lịch sử.
