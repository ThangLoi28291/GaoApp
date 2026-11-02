# UI-03c · Tài khoản, tiện ích và phản hồi trên điện thoại

Ngày 25/10/2026. **Đã viết mã và đối chiếu source; chờ người dùng chạy Visual Studio nghiệm thu.** Không chạy app, browser, build, bộ test hoặc migration trong lượt này. UI-02c–f và UI-03a–b vẫn chờ nghiệm thu như trước.

## Giao diện đã áp dụng

Mọi thay đổi thị giác nằm **dưới 768 px**, theo mẫu navy/lam v2. Một stylesheet `gaoapp.mobile-account-states.css` được nạp sau bộ mobile shell/viewport. Ngoài media query, file chỉ ẩn ba loại nội dung mới: nhãn nhóm tài khoản, nút về hồ sơ và hướng dẫn trang lỗi; không có rule thay bố cục desktop.

| Phần | Trình bày trên điện thoại | Luồng giữ nguyên |
|---|---|---|
| Menu tài khoản | Thẻ gần toàn chiều rộng, tên/vai trò/cửa hàng rõ; nhãn Cá nhân/Tiện ích; hàng chạm tối thiểu 52 px, có cuộn khi màn hình thấp | Cùng control, role menu, Arrow/Home/End/Escape, đóng khi click/focus ra ngoài |
| Hồ sơ | Header toàn chiều rộng, nút đóng 44 px; thẻ nhân viên và từng trường đọc theo một cột; tên/email dài xuống dòng | Dữ liệu profile hiện có theo phiên và cửa hàng, không thêm quyền chỉnh hồ sơ |
| Đổi mật khẩu | Cảnh báo phạm vi tất cả cửa hàng dễ thấy; input 16 px, cao 52 px; lỗi theo trường, trạng thái chờ/thành công/lỗi; nút về hồ sơ chỉ hiện trên điện thoại | Form, validation, hiện/ẩn mật khẩu, submit guard, CSRF, timeout và đăng nhập lại hiện có |
| Trợ giúp/tiện ích | Nội dung trợ giúp trong thẻ; lối tắt POS hiện có cùng kiểu hàng; slot tiện ích hỗ trợ vùng chạm và cuộn ngang nếu có control thật | Lối tắt vẫn từ `saleMenu` đã lọc và `Model.CanUsePos`; slot đang trống không được lấp bằng chức năng giả |
| Toast | Thẻ trắng, biểu tượng/màu trạng thái, chữ rõ, nội dung dài xuống dòng; nút đóng 44 px | Toastr/GaoAppNotify và TempData giữ nội dung, escape HTML, thời gian, loại và sự kiện cũ |
| Hộp xác nhận | SweetAlert modal có viền/radius, nội dung cuộn, nút xếp dọc và input phù hợp chạm; giữ vùng nhìn thấy khi bàn phím mở | Kết quả xác nhận/hủy, validation, challenge, handler và màu nút do nơi gọi quyết định |
| Từ chối truy cập | Thẻ 403, cảnh báo và đường dẫn bị chặn xuống dòng; nút một cột, thêm hướng dẫn liên hệ quản lý chỉ trên điện thoại | Điều kiện dashboard/returnUrl, URL nút và kiểm tra quyền ở điểm đến giữ nguyên |
| Lỗi quản trị/công khai | Thẻ thông báo theo v2, TraceId gọn và xuống dòng, nút tiếp tục một cột; không có header/footer template cũ trên điện thoại | Message/TraceId được render như trước, cùng URL và `data-csp-action="back"` |

Menu tài khoản tiếp tục là popup có cuộn, không chuyển thành một modal mới. JS chỉ bổ sung cách đặt chiều cao/vị trí theo `visualViewport` dưới breakpoint, đồng thời giới hạn tác động khi pinch zoom. Nút về hồ sơ gọi lại cơ chế chuyển panel cũ: việc rời form và xóa dữ liệu mật khẩu tuân theo hành vi hiện có; không tạo thêm form hoặc sao chép mật khẩu.

UI-03b được mở rộng nhận diện popup tài khoản và **SweetAlert modal** để trợ lý nhường pointer/focus và thanh ghim không tranh vùng hiển thị. SweetAlert dạng toast không được coi là modal. Bootstrap, native dialog, menu tài khoản và SweetAlert vẫn giữ cơ chế focus riêng; không thêm focus trap hoặc tự xác nhận thao tác.

## Phạm vi và bảo toàn

- `_CommonMasterLayout` chỉ thêm một import CSS. Component tài khoản chỉ thêm hai nhãn nhóm và một nút quay về; giữ nguyên tất cả ID, form, tên trường, dữ liệu, token, điều kiện quyền và URL cũ.
- `employee-account.js` chỉ thay trình bày popup điện thoại, thêm listener viewport có điều kiện và trạng thái nút quay về. Toàn bộ đoạn từ submit đổi mật khẩu đến xử lý pagehide/pageshow giống đầu lượt: không đổi payload, CSRF, chặn gửi lặp, xử lý 401/403/409, chuyển hướng cùng origin, xóa dữ liệu nhạy cảm hoặc guard phiên.
- Không sửa controller/service tài khoản, controller lỗi, challenge cookie, xác thực phiên, nguồn menu/quyền/cửa hàng hay CSP. Thay kiểu hộp xác nhận không thay điều kiện cho phép tác vụ ở nơi gọi.
- Ba trang có route thực được nâng là `Account/AccessDenied`, `Areas/Admin/Views/Error/Error` và `Views/Error/Error`. Hai layout cũ chỉ nạp CSS trang lỗi khi `ViewData["MobileStatePage"] is true`; CSS kiểm tra chính xác giá trị `data-mobile-state-document="true"`, không áp dụng cho các trang chưa opt-in. `viewport-fit=cover` chỉ thêm cho trang lỗi/từ chối, giữ zoom.
- Các nút điều hướng cũ của trang lỗi/từ chối vẫn còn nguyên; điểm đến tiếp tục tự kiểm tra quyền và có thể từ chối nếu tài khoản không đủ quyền. Không tạo đường dẫn chức năng mới hoặc cách vượt màn từ chối. Hai template `Shared/Error` được giữ nguyên để rà soát trong UI-03f; không tự xóa hay chuyển route ở bước này.
- Không tạo hộp thông báo lưu trữ, endpoint, menu/ghim, seeder, dữ liệu hay migration mới. `gaoapp.ui.js`, `_ToastMessages`, handler xác nhận tại module và CSS tài khoản cũ đều giữ hash.

## Đối chiếu source

[static-review.json](mobile-ui-20261025/evidence/ui-03c/static-review.json), so với snapshot `.artifacts/ui-03c/before/`:

- `node --check` đạt cho `employee-account.js` và `gaoapp.mobile-viewport.js`.
- **24 file bảo vệ giữ hash**, gồm các controller/service menu và tài khoản, CSP/phiên, handler toast, hai Shared/Error và các module giao dịch POS/nhập hàng.
- **9 file cũ được đối chiếu toàn phần** ngoài các thay đổi trình bày đã khai báo; chuẩn hóa BOM/CRLF và newline cuối file khi so sánh text. Các ID và đích liên kết cũ còn nguyên; source submit mật khẩu được đối chiếu riêng.
- CSS ngoài breakpoint chỉ ẩn control/nội dung mới, phần còn lại nằm hoàn toàn trong media điện thoại; không nâng z-index hoặc đổi handler/thời gian thông báo.
- [Baseline delta](mobile-ui-20261025/evidence/ui-03c/baseline-delta.json): 560 file gốc, **531 không đổi, 29 đổi, 0 mất**, tính cộng dồn các bước trước. Một asset CSS mới được ghi riêng; baseline gốc không sửa.

Đây là bằng chứng cú pháp và source, chưa xác nhận Razor build hoặc giao diện runtime. [16 ca nghiệm thu](mobile-ui-20261025/evidence/ui-03c/acceptance-matrix.json) đều chưa chạy. Không có kết luận đã đạt về bàn phím thật, animation, màu sắc hoặc không đổi pixel desktop chỉ từ kiểm tra source.

## Bạn chạy Visual Studio để tự kiểm tra

Mở solution, chọn Web rồi **Visual Studio → F5**. Thử 320/390/430 px và 768/1024/1366 px; dùng Safari iPhone/Chrome Android cho bàn phím và safe area thực.

1. Tại trang quản trị, mở avatar: đúng tên/vai trò/cửa hàng, menu dài cuộn được. Thử Arrow/Home/End/Escape, Tab và chạm ra ngoài. Mở hồ sơ/trợ giúp, kiểm tra tên/email dài; nút đóng và quay về hồ sơ dùng được.
2. Với **tài khoản dùng để kiểm tra**, mở đổi mật khẩu: thử xác nhận không khớp, mật khẩu hiện tại sai và hiện/ẩn từng ô. Không gửi hợp lệ chỉ để xem giao diện nếu chưa muốn đổi mật khẩu. Khi chủ động đổi thành công, xác minh đăng nhập lại và thu hồi phiên theo luồng cũ; khi lỗi/mạng chậm, không tự gửi lại.
3. Đang ở form, dùng nút về hồ sơ hoặc đóng drawer; kiểm tra dữ liệu mật khẩu được xóa theo hành vi cũ. Thử guard đang lưu. Mở account từ POS admin drawer, kiểm tra đóng/mở drawer cha; thử thêm POS legacy và phiếu nhập hàng.
4. Tạo thông báo thành công/lỗi/cảnh báo/thông tin từ thao tác sẵn có: chữ dài, đóng bằng chạm/bàn phím, progress/timer như trước, nội dung không trở thành HTML. Kiểm tra hộp xác nhận sẵn có: hủy giữ trạng thái, xác nhận mới gọi tác vụ; điều kiện quyền/challenge không mất.
5. Mở trợ lý rồi account hoặc hộp xác nhận: trợ lý nhường tương tác, trở lại sau khi đóng. Native dialog sửa thông tin của trợ lý vẫn dùng được. Thử bàn phím và màn ngang thấp trong popup/modal.
6. Thử `/admin/account/access-denied` với returnUrl dài; so sánh tài khoản đủ/thiếu quyền qua luồng hiện có. Trang `/error/404?isAdmin=1` và `/error/500` hiển thị cùng dữ liệu lỗi/TraceId; Quay lại và các liên kết cũ vẫn dùng cơ chế cũ. Những URL này chỉ mở trang thông báo, không chứng minh mã HTTP hay lỗi nghiệp vụ thực.
7. Ở đúng 768 px trở lên: menu, drawer, toast, xác nhận và các trang lỗi giữ bố cục desktop/tablet cũ; nhãn/nút/hướng dẫn mới không hiện. Các trang legacy ngoài lỗi không nhận CSS này.
8. Thử xoay máy, đóng/mở bàn phím, pinch zoom, phóng chữ, giảm chuyển động và Back/Forward. Đổi cửa hàng/tài khoản theo luồng hỗ trợ: profile/lối tắt đúng cấu hình, không tự sinh chức năng hoặc quyền.

Không cần migration cho UI-03c. Bước tiếp theo sau phản hồi là **UI-03d: trang tổng quan và lối tắt theo dữ liệu/quyền hiện có**.
