# UI-03b · Vùng hiển thị, bàn phím và lớp phủ điện thoại

Ngày 25/10/2026. **Đã viết mã và đối chiếu source, chờ người dùng chạy Visual Studio nghiệm thu.** Chưa chạy app, browser, build, bộ test hoặc migration. UI-02c–f và UI-03a tiếp tục giữ trạng thái chờ nghiệm thu, không được đánh dấu đạt runtime bởi bước này.

## Phần đã áp dụng

Giao diện mới chỉ hoạt động **dưới 768 px** trên các khung đã tích hợp UI-03a. `_CommonMasterLayout` nạp thêm một CSS/JS sau bộ shell, đồng thời thêm `viewport-fit=cover` để dùng safe area; không chặn pinch zoom hoặc giới hạn mức phóng to.

`gaoapp.mobile-viewport.js` theo dõi vùng nhìn thấy qua `visualViewport`, có fallback về kích thước cửa sổ. Các sự kiện được gộp theo animation frame; không polling. Chiều cao header/dock lấy từ phần tử thật, không tạo một dock hay form thứ hai. Khi ra khỏi breakpoint, các biến trình bày và trạng thái trợ lý do adapter đặt được khôi phục.

| Ngữ cảnh | Điều chỉnh trên điện thoại | Chủ sở hữu được giữ |
|---|---|---|
| Trang quản trị | Safe area hai bên; khoảng đáy theo thanh ghim; tạm ẩn thanh ghim khi nhập liệu có bàn phím hoặc mở lớp phủ | Menu/ghim và form hiện có |
| Menu quản trị | Chiều cao theo vùng nhìn thấy; khóa body trên điện thoại, lưu và trả vị trí cuộn khi đóng hoặc rời breakpoint | Controller menu cũ tiếp tục giữ focus, Escape và Tab |
| Tài khoản, POS admin drawer | Chiều cao trong vùng nhìn thấy, safe area, body cuộn riêng | Bootstrap tiếp tục giữ focus và khóa cuộn |
| Modal thường, dialog xem lại/sửa thông tin | Giới hạn chiều cao, mép an toàn; đưa trường nhập đang bị che vào vùng cuộn phù hợp | Modal/native dialog hiện có; không thêm focus trap |
| Nhập hàng | Khoảng nội dung theo header/dock thật; tạm ẩn dock khi bàn phím xuất hiện; giữ khoảng đáy đã đo để giảm nhảy nội dung | Form nhận hàng, hai tab, scanner, lưu và retry hiện có |
| POS Prime | Bổ sung safe area; dùng dock thật để đặt trợ lý | POS giữ toàn bộ geometry, sheet, thanh toán và xử lý focus riêng; adapter không ghi biến `--pos-prime-visible-*` |
| Trợ lý | Launcher tránh dock; panel theo vùng nhìn thấy; tạm ẩn và inert khi menu/modal bên ngoài mở | Hội thoại và handler trợ lý hiện có; native dialog của chính trợ lý vẫn tương tác được |
| Toast | Bố trí dưới header đang hiện, giới hạn chiều cao và cuộn khi nhiều thông báo | Toastr giữ nội dung, thời gian, sự kiện và thứ tự lớp hiện có |

Khi không còn đủ chiều cao để đặt panel trợ lý phía trên dock, panel đang mở dùng gần toàn vùng nhìn thấy để giữ nút đóng và vùng soạn tin có thể tiếp cận. Đây là lớp hội thoại phủ trên trang; không tạo thêm vùng thanh toán. Việc bấm ra ngoài có thể đóng trợ lý theo hành vi cũ.

Trợ lý nhường Escape/pointer khi đang inert; trên điện thoại không lấy Escape đã được xử lý hoặc thuộc native dialog của nó. Không sửa nội dung hội thoại, gọi mạng, xác nhận tác vụ hay quyền của trợ lý. Toast toàn cục vẫn thuộc document; phản hồi trong native dialog tiếp tục do dialog sở hữu, không chuyển toast vào native top layer.

## Cuộn, focus và ranh giới bảo toàn

- Nhận diện bàn phím là **ước lượng** từ focus trường nhập và phần chiều cao bị thu hẹp; cần kiểm tra Safari/Chrome trên thiết bị thật. Không coi việc giảm thanh địa chỉ là luôn mở bàn phím. Khi pinch zoom, adapter bỏ các metric và không tự kéo trường đang nhập.
- Chỉ cuộn khi focus hoặc kích thước vùng nhìn thấy thay đổi; không tự cuộn lại trong mỗi lần người dùng kéo trang. Ưu tiên vùng cuộn gần trường nhập, không gọi focus/blur, không sửa giá trị/caret hoặc submit.
- Không cuộn nội dung phía sau một modal đang mở. Menu giữ vùng tìm kiếm riêng; POS Prime giữ cơ chế cuộn form/thanh toán hiện có. Dock ở phía sau lớp phủ không làm giảm vùng nhập của modal.
- Chỉ đồng bộ **chế độ app nhập hàng** từ `max-width: 768px` xuống `767.98px` ở một media query CSS và một matchMedia JS. Tại đúng 768 px dùng nhánh ngoài app như shell chung; các rule responsive cũ khác của nhập hàng vẫn giữ nguyên. Nội dung/form/chuyển tab/lưu phiếu không đổi.
- Không sửa giao diện desktop, source controller/service, quyền, cấu hình cửa hàng, nguồn `AdminMenuItems`, số tiền, CSRF hay giao thức lưu/thử lại. Không tạo menu/ghim mặc định, dữ liệu, migration hoặc endpoint mới.

## Đối chiếu đã thực hiện

[static-review.json](mobile-ui-20261025/evidence/ui-03b/static-review.json) đối chiếu với snapshot đầu lượt `.artifacts/ui-03b/before/`:

- `node --check` đạt cho bốn JS: viewport mới, menu, trợ lý và adapter nhập hàng.
- **21 file bảo vệ giữ hash**, gồm các source menu/quyền, tài khoản, POS, CSS nền và baseline.
- **5 file cũ được so sánh toàn phần** ngoài thay đổi đã khai báo: master chỉ meta/hai import; menu chỉ khóa cuộn điện thoại; trợ lý chỉ hai listener; CSS/JS nhập hàng chỉ breakpoint app.
- CSS mới nằm hoàn toàn trong `max-width: 767.98px`. Adapter mới không có network/storage/clone/form submit hay focus trap thứ hai.
- [Baseline delta](mobile-ui-20261025/evidence/ui-03b/baseline-delta.json): 560 file gốc, **537 không đổi, 23 đổi, 0 mất**, cộng dồn các bước trước. Hai asset viewport mới được ghi riêng, không thay hash baseline.

Đây là kiểm tra cú pháp và source; chưa xác nhận Razor build, hiển thị, bàn phím hoặc cuộn thực tế. [16 ca nghiệm thu](mobile-ui-20261025/evidence/ui-03b/acceptance-matrix.json) đều đang chờ người dùng chạy.

## Bạn chạy Visual Studio để tự kiểm tra

Mở solution, chọn Web rồi **Visual Studio → F5**. Thử 320/390/430 px, sau đó 768/1024/1366 px. Với bàn phím và tai thỏ, thử thêm Safari iPhone/Chrome Android theo cách truy cập máy phát triển đang dùng.

1. Trang quản trị dài: cuộn xuống giữa trang, mở Menu, tìm có dấu/không dấu và cuộn đến mục cuối; đóng bằng nút/Escape. Trang trở về vị trí cũ, focus về nút mở; không còn khóa cuộn sau khi đóng.
2. Tại trường nhập gần đáy, mở/đóng bàn phím, thử bàn phím vật lý và chuyển trường. Trường đang nhập tiếp cận được, thanh ghim nhường chỗ; nội dung/giá trị giữ nguyên. Xoay máy và thu/mở thanh địa chỉ rồi thử lại.
3. Mở tài khoản/đổi mật khẩu, modal hiện có và dialog xem lại menu. Thử nhập trường cuối, cuộn, Tab/Escape, hủy và mở lại; Bootstrap/native dialog vẫn giữ hành vi cũ.
4. Mở trợ lý ở trang thường, POS và phiếu nhập; kiểm tra launcher/panel với dock. Mở menu/modal khi trợ lý đang hiện: trợ lý không lấy thao tác hoặc focus của lớp phía trước. Mở dialog sửa thông tin của chính trợ lý và kiểm tra đóng dialog trước bằng Escape.
5. Trên phiếu nhập hàng, nhập số lượng trong sheet, đóng bàn phím rồi kiểm tra dock/tab/lưu theo luồng cũ. Không có thanh ghim chung đè dock. Thử phiếu chỉ xem và phiếu có quyền chỉnh.
6. POS Prime và legacy: giỏ hàng giữ nguyên, account/Menu mở được, thao tác nhập và thanh toán vẫn dùng dock/modal cũ. Kiểm tra Prime khi bàn phím xuất hiện trong sheet thanh toán, tránh cuộn kép.
7. Dùng thông báo thành công/lỗi sẵn có để kiểm tra toast, kể cả khi có modal; thử màn ngang thấp, pinch zoom, phóng chữ và chế độ giảm chuyển động. Nút đóng và nội dung vẫn tiếp cận được.
8. Đổi 767 → 768 → 1024 → 1366 px khi menu/popup đang mở; đóng và thử cuộn tiếp. Desktop giữ bố cục cũ, không còn style khóa body hoặc trợ lý inert do adapter. Kiểm tra thêm Back/Forward theo guard phiên hiện có, đổi cửa hàng/tài khoản qua luồng được hỗ trợ và menu/quyền tương ứng.

Không cần migration mới cho UI-03b. Sau phản hồi hoặc nghiệm thu, bước tiếp theo là **UI-03c: tài khoản nhân viên, tiện ích, phản hồi thành công/lỗi và trang từ chối/lỗi trên điện thoại**.
