# UI-03e · Đăng nhập điện thoại và mở thêm Chrome khi chạy local

Ngày 25/10/2026. **Đã viết mã, kiểm tra cú pháp JS và đối chiếu source; chờ người dùng chạy Visual Studio.** Không chạy app, Chrome, build, bộ test hoặc migration. Các bước UI trước tiếp tục chờ nghiệm thu.

## Giao diện điện thoại

Login dùng bố cục một cột **dưới 768 px**, nền sáng và navy/lam v2: thương hiệu gọn, ngữ cảnh cửa hàng/máy POS, thẻ đăng nhập, input 16 px/cao 52 px, nút Hiện/Ẩn mật khẩu và nút đăng nhập rõ trạng thái. Tên cửa hàng/quầy dài xuống dòng; vùng đệm dùng safe area. Điện thoại không hiển thị cảnh Halloween hoặc chạy vòng vẽ canvas; atlas chỉ tải khi cần bản desktop.

Desktop/tablet từ 768 px giữ nguyên CSS Halloween, ngày/đêm, cảnh và cách soi mật khẩu bằng đèn pin. CSS cũ không sửa; file mới ngoài media chỉ ẩn hai phần thương hiệu/nhãn được thêm. Khi đổi breakpoint, adapter dừng/khôi phục hiệu ứng và trả các biến viewport về trạng thái cũ; mật khẩu được che lại khi rời điện thoại.

Điện thoại dùng **cùng input mật khẩu**, chỉ đổi `type` để hiện/ẩn. Adapter không đọc `.value`, không sao chép mật khẩu, không gọi mạng hoặc lưu storage. Khi submit, ẩn tab, blur/pagehide thì che lại mật khẩu. Không dựng form, nút gửi hoặc cơ chế xác thực thứ hai.

Khu vực lỗi tổng có role alert/focus; lỗi theo trường và thông báo đổi mật khẩu thành công giữ dữ liệu gốc. Trang có lỗi máy chủ có thể focus phần tổng lỗi trên điện thoại; validation client tiếp tục do jQuery/native form sở hữu. Input giữ autofill và có gợi ý phím Tiếp/Đi. `visualViewport` giúp cuộn trường đang focus/khối lỗi vào vùng nhìn thấy; không liên tục kéo lại khi người dùng cuộn và không tự cuộn khi pinch zoom. Nút chờ có trạng thái busy; khi trở lại form từ bfcache trên điện thoại, trả nút về đúng nhãn đã render.

## Xác thực và cửa hàng giữ nguyên

- Giữ nguyên controller/service đăng nhập, LoginVm, rate limit, CSRF, cookie/session, audit, ReturnUrl/local redirect và xác thực thiết bị.
- Giữ nguyên tất cả trường/binding, điều kiện `ManageTerminals`, `RequireTerminalPairing`, danh sách máy POS theo cửa hàng, khóa ghép máy và tên thiết bị. Không ghép hoặc chọn quầy tự động.
- Handler submit cũ vẫn kiểm tra native/jQuery validation, khóa nút và POST cùng form. Adapter điện thoại không preventDefault, submit lại hoặc thay payload.
- Tenant/terminal middleware, nguồn menu/quyền và offline privacy script không sửa; không tạo menu, tài khoản, ghim, endpoint nghiệp vụ hoặc migration.

## Mở Chrome mà không giao cửa sổ cho debugger

Hai profile Windows **`http` và `https`** đổi `launchBrowser` thành false và bật cờ local `GaoAppDevBrowser__Enabled`. Các URL, môi trường và cấu hình khác giữ nguyên; profile đang lưu trong `.csproj.user` vẫn là `https`. Tên profile `https` trước lượt này đã dùng `http://localhost:7051`; bước này không đổi binding/chứng chỉ.

`DevelopmentChromeLauncher` được đăng ký một lần ngay trước `app.Run`, chỉ hoạt động khi cả ba điều kiện đạt: **Windows + Development + cờ opt-in**. Sau `ApplicationStarted`, helper chọn địa chỉ HTTP(S) loopback thực của server, tìm Chrome ở các thư mục cài đặt tiêu chuẩn và mở `--new-window` tới `/admin`.

Helper không dùng remote debugging/profile riêng, không liệt kê hoặc dừng process, không sửa tab/profile/cookie và không có callback đóng Chrome khi server dừng. `Dispose` chỉ giải phóng process handle do .NET giữ. Nếu thiếu Chrome/địa chỉ phù hợp hoặc không mở được, chỉ ghi thông báo và để Web tiếp tục chạy. Đã xác nhận có `chrome.exe` trong một thư mục tiêu chuẩn bằng kiểm tra file, **chưa khởi chạy executable**.

Microsoft mô tả việc Visual Studio đóng browser phụ thuộc các lựa chọn script debugging/vòng đời browser. Ở đây tránh thay thiết lập toàn IDE bằng cách tắt phần mở browser của IDE trong hai profile và mở Chrome thường từ helper Development. Tham khảo [Microsoft về browser/debugger](https://devblogs.microsoft.com/dotnet/changes-to-script-debugging-in-visual-studio-15-7/), [launch profile](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-10.0) và [ProcessStartInfo](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo?view=net-10.0).

Phạm vi là hai profile `http`/`https` đang dùng trên Windows; IIS Express/WSL và thiết lập Visual Studio cá nhân giữ nguyên. Không có thao tác mở/đóng Chrome hiện tại trong lượt này. Chạy build đơn thuần không mở Chrome; Production/Staging hoặc khi cờ false không mở Chrome. Khi tự động hóa bằng profile local, có thể truyền `--GaoAppDevBrowser:Enabled=false` cho app để tắt tính năng này.

## Bằng chứng source

[static-review.json](mobile-ui-20261025/evidence/ui-03e/static-review.json) đối chiếu snapshot `.artifacts/ui-03e/before/`:

- Ba JS đã kiểm tra cú pháp: account-login cũ, adapter mobile mới và browser test đã cập nhật.
- **18 file bảo vệ giữ hash**, gồm CSS desktop, controller/VM đăng nhập, session/rate limit/tenant/terminal, offline privacy, menu/layout/CSP, project/user settings và unit test hình học đèn pin.
- **5 đối chiếu file cũ**: view chỉ thêm điểm tích hợp/branding/thuộc tính; legacy JS chỉ thêm guard breakpoint và vòng đời canvas; Program chỉ thêm một lệnh đăng ký helper; launchSettings chỉ đổi hai profile như trên; browser test chỉ cập nhật hợp đồng phone. Text được chuẩn hóa BOM/CRLF/newline cuối file khi so sánh.
- [Baseline delta](mobile-ui-20261025/evidence/ui-03e/baseline-delta.json): 560 file gốc, **529 không đổi, 31 đổi, 0 mất**, cộng dồn các bước. Ba file mới CSS/JS/helper ghi riêng; baseline không sửa.

Chưa build C#/Razor hoặc chạy test/browser. Browser test cũ đã chuẩn bị kiểm tra giao diện phone mới; unit test hình học giữ nguyên. Không khẳng định đã kiểm tra pixel desktop, bàn phím thật hoặc hành vi Chrome khi F5/Stop chỉ bằng source. [18 ca nghiệm thu](mobile-ui-20261025/evidence/ui-03e/acceptance-matrix.json) đều chờ người dùng chạy.

## Bạn chạy Visual Studio để tự test

1. Giữ các cửa sổ/tab Chrome đang dùng. Trong Visual Studio chọn Web và profile **`https` hoặc `http` → F5**. Web khởi động xong phải mở thêm cửa sổ Chrome, tab cũ còn nguyên. Bấm Stop: Web dừng, các cửa sổ Chrome vẫn giữ. Lặp lại F5 để kiểm tra mở thêm; không cần bật JavaScript debugger cho browser mới.
2. Mở `/admin/account/login` ở 320/390/430 px: kiểm tra tên cửa hàng/quầy, tài khoản/mật khẩu, Hiện/Ẩn và keyboard/autofill. Thử trường cuối khi bàn phím mở, xoay máy, pinch zoom, phóng chữ và reduced motion. Điện thoại không có cảnh/canvas chạy.
3. Thử thiết bị đã ghép, chưa ghép, nhập khóa hoặc chọn máy/tên thiết bị, và nhánh quản lý máy POS dành cho admin. Các điều kiện/quyền/kết quả ghép phải như trước; không thao tác ghép hoặc đăng nhập bằng tài khoản thật chỉ để xem bố cục.
4. Với tài khoản kiểm tra, thử thiếu trường, sai mật khẩu, lỗi máy chủ/rate limit, ReturnUrl hợp lệ/không hợp lệ và thông báo sau đổi mật khẩu. Đối chiếu validation, focus lỗi, trạng thái chờ, cookie/session và scope cửa hàng/quầy theo luồng cũ.
5. Thử Back sau submit để kiểm tra nút phone được trả trạng thái; ẩn/đổi tab hoặc đổi breakpoint khi đang hiện mật khẩu phải che lại. Tại 768/1024/1366 px, bản Halloween và đèn pin vẫn theo giao diện desktop cũ.

Không cần migration. Sau phản hồi, tiếp tục **UI-03f: rà layout/navbar cũ và Shared/Error trước quyết định giữ, thay hoặc dọn mã**.
