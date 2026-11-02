# WEB03 — Giao diện xem và chọn hàng

Ngày: 31/10/2026. Trạng thái: **PASS** — production/test V5; 359/359 test và review độc lập đạt, không còn P0/P1/P2 trong phạm vi WEB03. Website chưa mở bán.

Phạm vi theo [kế hoạch](STOREFRONT-IMPLEMENTATION-PLAN-20261030.md): W03T01–W03T04. Vai trò độc lập: `storefront_code` viết production, `storefront_tests` lập oracle trước khi đọc API và viết/chạy test, `storefront_review` rà nguồn, chạy probe và xem ảnh thực tế; root điều phối, build và ghi nhận cổng.

## Kết quả triển khai

- Project khách riêng `GaoApp.Storefront.Web`: trang chủ, danh mục, chi tiết, giỏ và bốn đích điều hướng. Không tham chiếu Web quản trị, không tự tạo menu.
- Danh mục dùng hàng đã đăng của đúng cửa hàng, tìm tiếng Việt không dấu kể cả đ/Đ, nhóm trưng bày, thứ tự cửa hàng/tên tăng/tên giảm và phân trang. Giá theo chính sách bán lẻ POS; có lựa chọn chỉ bán theo thùng hoặc đơn vị quy đổi.
- Giao diện nền trắng, chữ đậm, khối xanh chữ trắng; Inter tiếng Việt và Latin tải từ host. Banner giữa dùng “Chọn món hợp ý. / Vừa túi tiền.”. Có trạng thái tải/rỗng/lỗi, nhãn bàn phím, focus và giảm chuyển động.
- Giỏ lưu ID và số lượng theo phạm vi cửa hàng/host trên thiết bị. Máy chủ đọc lại tên, giá và trạng thái; không tin giá hoặc StoreId từ client. Kiểm tra chống giả mạo yêu cầu, giới hạn 100 lựa chọn, số lượng dương tối đa 1.000.000 với bốn số lẻ, phép tính có kiểm tra tràn và giới hạn tiền chung. Tiền từng dòng làm tròn VND nguyên như POS; chuỗi hiển thị tiền do máy chủ định dạng để không mất chữ số qua JavaScript.
- Media được đọc qua `/shop/media/{id}`, kiểm tra xuất bản/phạm vi, gốc uploads, đường dẫn vật lý, reparse point, loại và kích thước tệp. Không mở toàn bộ uploads bằng static files.

Production vẫn đóng. Bản xem trước chỉ hoạt động khi đồng thời Development, có cờ cấu hình máy chủ, bind loopback, peer/local loopback, hostname và subdomain cửa hàng hợp lệ, cấu hình cửa hàng bật trong dữ liệu thử. Không có tham số client để đổi cửa hàng. WEB03 không tạo đơn, giữ hàng, thu tiền hay ghi kho; checkout/tra cứu hiện giải thích trạng thái chờ triển khai WEB04.

## Sửa lỗi theo quy trình

| Mã | Nhận định | Sửa và bằng chứng cần đạt |
|---|---|---|
| W03R1 | Tiền số lượng lẻ làm tròn hai số lẻ gây lệch số hiển thị | Làm tròn từng dòng 0 số lẻ AwayFromZero; hai dòng 25.000 × 0,0001 phải là 3 + 3 = 6 đồng |
| W03R2 | Chỉ kiểm tra tràn decimal, chưa giới hạn tiền nghiệp vụ | Chặn dòng và tổng từ `MoneyLimit` trở lên; giữ kiểm tra tràn |
| W03R3 | Phản hồi thêm giỏ chậm ghi đè thay đổi ở tab khác | So revision/nội dung lưu mới ngay trước commit; có thay đổi thì giữ bản mới và yêu cầu thử lại |
| W03R4 | ID lưu trên thiết bị lớn hơn Int32 làm giỏ mắc lỗi 400 | Loại từng dòng không hợp lệ/trùng, giữ dòng đúng và báo khôi phục |
| W03R5 | Razor hiểu biến `@section` là directive, build không đạt | Đổi thành `navSection`, dùng biểu thức Razor tường minh; build lại đạt |
| W03R6 | Focus nút mua bị thanh điều hướng che 4,625px ở 320×844 | Chừa vùng cuộn và đo vị trí focus/viewport/thanh dưới, cuộn tức thời có khoảng hở |
| W03R7 | Nút Xóa chỉ rộng 41,546875px | Đặt kích thước tối thiểu 44×44px; đo lại trên cả năm chiều rộng |

Lỗi bộ tạo dữ liệu thử được ghi riêng: chuẩn hóa đường dẫn Windows trước kiểm tra containment; assertion giữ nguyên. Một thao tác dọn sau test hai tab chờ response khi điền lại cùng số lượng không phát sinh thay đổi đã được đổi thành kiểm tra số lượng rồi thao tác Xóa qua UI. Không sửa production hoặc giảm assertion để che hai lỗi harness này.

## Kiểm thử và review

Kết quả cuối **359/359 đạt, 0 lỗi, 0 bỏ qua**: 51 WEB03, 170 hồi quy WEB01 và 138 hồi quy WEB02. Browser nằm trong một ca xUnit, gồm 20 bước qua ba pha, 37 trạng thái DOM và 46 ảnh chụp ở 320/360/390/430/1440px. Chữ chính 16px, Inter nội bộ tải đủ, tương phản đo thấp nhất 6,5713:1; không tràn ngang, không nút nhỏ hơn chuẩn đã đặt, không lỗi JavaScript. Một request lỗi mạng là ca cố ý ngắt kết nối; thao tác thử lại đạt.

Lượt đầu: 49/51 đạt, hai lỗi fixture xảy ra trước media/browser. Lượt browser tiếp theo tìm ra W03R6; bản V4 sửa focus đạt và kiểm tra được giỏ/tiền/số lượng/đổi đơn vị, sau đó tìm ra W03R7. Toàn bộ đã được sửa và chạy lại trên bản V5 cuối, reviewer xác nhận đóng W03R1–W03R7.

Reviewer đã chạy độc lập probe JS cho R3/R4 và 14 kiểm tra đọc media trên DLL đã build, gồm file thiếu/MIME/đường dẫn/junction; không chạm database hoặc uploads vận hành. Ảnh chụp và đo DOM trên website thật dùng dữ liệu SQL tổng hợp, có ảnh fixture 1px; đây là bằng chứng layout/luồng/đọc media, không phải nghiệm thu ảnh hàng thật của cửa hàng.

Nguồn và bằng chứng lưu tại `.artifacts/storefront-rollout/WEB03/`: production V5 gồm 27 đường dẫn, test V5 gồm năm file C# và một harness Node; cả nguồn và sáu DLL có 0 sai lệch hash sau chạy. Canonical browser GUID: `c0a938c9b5964a42b2595e6981703809`. Bản chụp 33 file nguồn/test tại `coordinator/frozen-v5` đã đối chiếu hash trước bước tiếp theo. Build tách biệt ở `.artifacts/storefront-rollout/build`; không dừng hoặc ghi đè DLL của tiến trình khác.

Database trước/sau giữ đúng năm GUID fixture có sẵn, không còn database thử mới hoặc host/Node thử đang chạy. Đã dọn 17 mục media thuộc fixture/18 tệp sau kiểm tra path/hash/GUID/reparse; không đụng uploads hoặc database vận hành. Log, TRX, ảnh và báo cáo được giữ lại.

Bản báo cáo lưu trong repository: [test độc lập](storefront-web03/test-final.txt), [review độc lập](storefront-web03/review-final.md), [kết quả](storefront-web03/test-results.json), [browser](storefront-web03/browser-summary.json), [identity/cleanup](storefront-web03/identity-cleanup.json), [findings đã đóng](storefront-web03/findings.json). Lệnh chính: restore offline bằng NuGet.Config của validator, build với `--artifacts-path .artifacts/storefront-rollout/build`, rồi `dotnet test --no-build --no-restore` lần lượt các category `StorefrontWeb03`, `StorefrontWeb01`, `StorefrontWeb02` trên chính DLL đã khóa. SQL/discovery/browser chạy với quyền công cụ cần thiết cho fixture SQLEXPRESS, không dùng database cửa hàng.

## Giới hạn và bước tiếp

- Revision giỏ bảo vệ phản hồi chậm sau thay đổi quan sát được ở tab khác; không tuyên bố localStorage có giao dịch tuần tự hóa cho mọi thao tác xảy ra đúng đồng thời. Giỏ chưa có tác động tài chính hoặc kho.
- Animation/3D, kiểm tra payload model và đóng gói giấy phép font cuối thuộc WEB08. Nghiệm thu dữ liệu/cấu hình và mở bán thuộc WEB09.
- Cổng test/review/identity/cleanup đã đạt; được chuyển WEB04. Đường dẫn khách trong bản xem trước: `/shop`, `/shop/products`, `/shop/products/{id}`, `/shop/cart`, `/shop/checkout`, `/shop/orders`.
