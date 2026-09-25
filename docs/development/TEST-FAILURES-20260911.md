# Sửa 73 failed và 2 skipped trong Visual Studio

Nguồn đối chiếu: Test Explorer có 2.243 kết quả: 2.168 passed, 73 failed, 2 skipped. Đã đọc bản sao `.vs/GaoApp/v18/TestStore/0/256.testlog` bằng bộ đọc TestLog của Visual Studio; danh sách và lỗi gốc nằm tại `TestResults/full-suite-fix/visual-studio-results.json`.

## Nguyên nhân và thay đổi

- **45 lỗi đường dẫn Web:** `FullApplicationFixture` chỉ hỗ trợ `--artifacts-path`. `TestApplicationBuild` chọn đúng Web build đi cùng test cho cả `bin/Debug/net8.0`, `bin/Release/net8.0` và bố cục SDK artifacts. Không lấy nhầm DLL từ ứng dụng đang chạy.
- **12 lỗi seed dữ liệu cũ:** model `Store` hiện tại có ba cột thông tin in hóa đơn chưa tồn tại tại migration đang kiểm tra. `LegacyStoreSeed` ghi các cột baseline bằng SQL có tham số rồi lấy ID; giữ nguyên kiểm tra nâng cấp, bảo toàn dữ liệu và rollback.
- **14 lỗi kỳ vọng migration:** bổ sung năm migration mới vào danh sách kỳ vọng; kiểm tra migration ngân hàng nhà cung cấp theo chính target model của migration đó, đồng thời vẫn kiểm tra current model/snapshot và schema.
- **Một lỗi phạm vi UI:** bổ sung hai view InvoiceInputStock và MediaLibrary đã triển khai vào danh sách sử dụng design system.
- **Một lỗi catch rỗng:** xử lý kết thúc tường minh khi đường dẫn ảnh cần xóa vốn không còn tồn tại; các lỗi quyền truy cập / I/O khác vẫn được báo ra.
- **Hai test Production bị skipped:** dùng `Fact` thông thường. Nếu chưa đặt `GAOAPP_TEST_RELEASE`, tự tạo một cặp Web/Migrator từ nguồn hiện tại và xác minh package qua script publish có sẵn, dùng chung trong một test run. Log và kết quả publish ghi ra file; không phụ thuộc EOF của pipe từ build server nền. Không triển khai lên IIS hoặc server.

## Cấu hình SQL test theo máy

`GAOAPP_TEST_SQL_SERVER` cho phép chọn một SQL Server cục bộ; mặc định vẫn là `(localdb)\MSSQLLocalDB`. Các fixture giữ nguyên tên database GUID với tiền tố test và kiểm tra tên trước khi cleanup.

Trên máy hiện tại, LocalDB gặp lỗi khởi động Windows. File `GaoApp.Tests/test.local.runsettings` chọn `.\SQLEXPRESS`; project tự đọc file này khi không có RunSettingsFilePath khác. File được git-ignore, không chứa mật khẩu, không cố định tên máy và không đi vào cấu hình ứng dụng/publish. Test khóa ACB cũng dùng lựa chọn SQL test chung, trừ khi đã có override riêng.

Hai test Production có thể dùng package được chuẩn bị sẵn qua `GAOAPP_TEST_RELEASE`; nếu không, lần chạy đầu sẽ thêm thời gian restore/publish. Các package và database đều phục vụ kiểm tra riêng.

## Phạm vi xác minh

Theo yêu cầu sau cùng, chỉ chạy lại các trường hợp failed/skipped, không chạy lại All. Bộ lọc được trích từ báo cáo Visual Studio:

- `TestResults/full-suite-fix/failed-only.filter`: 73 kết quả failed, thuộc 63 phương thức.
- `TestResults/full-suite-fix/failed-and-skipped.filter`: 75 kết quả, thuộc 65 phương thức.
- Kết quả riêng: `failed-targeted.trx` và `published-targeted.trx` trong cùng thư mục.
- Log: `Logs/failed-tests-73.log`, `Logs/failed-tests-published.log`; publish: `TestResults/security-phase6/automatic-publish.log`.

Kết quả cuối ngày 11/09/2026:

- Nhóm 73 failed cũ: **73/73 passed**, chạy bằng output Debug thông thường của Visual Studio, khoảng 8 phút 31 giây.
- Nhóm 2 skipped cũ: **2/2 passed**, thực sự publish và chạy kiểm tra Production, khoảng 1 phút 8 giây.
- Đối chiếu tên từng kết quả trong hai file TRX với danh sách gốc: **đúng đủ 75 trường hợp**, không thiếu/thừa; **0 failed, 0 skipped**.
- Build Debug cuối cùng thành công: **0 errors**, còn 10 cảnh báo xUnit có sẵn; log `Logs/failed-tests-final-debug-build.log`.
- Chưa xác nhận lại toàn bộ suite; người dùng sẽ tự chạy All.

Sau khi cập nhật mã, Rebuild trong Visual Studio rồi Run All để người dùng xác nhận toàn bộ suite.
