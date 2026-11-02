# Triển khai tuần tự WEB02 đến WEB09

Yêu cầu của chủ cửa hàng: làm từng bước theo plan, code/test/review độc lập; đạt mới chuyển bước, lỗi phải phân tích, sửa, chạy test và review lại; tự quyết định kỹ thuật đến khi hoàn thành.

Mốc lịch sử ngày 01/11/2026: đồng ý xóa đúng danh sách 1.510 XML test WEB07 và chuyển WEB08 theo quy trình độc lập. Việc xóa đã hoàn thành và review độc lập đạt; 1.272 XML context-only giữ nguyên. WEB08 đã đạt phần triển khai sau code/test/review độc lập; điều kiện hàng thật, thiết bị và nghiệm thu trước phát hành còn mở. Tại mốc lịch sử này, WEB09 chưa bắt đầu. Hồ sơ các bước trước và gate chờ trước đồng ý được giữ nguyên theo thời điểm.

Nền đầu vào: [WEB00](STOREFRONT-WEB00-CONTRACT-20261030.md), [WEB01 đã đạt](STOREFRONT-WEB01-20261030.md), [kế hoạch chức năng](STOREFRONT-IMPLEMENTATION-PLAN-20261030.md). Website khách nằm ở `GaoApp.Storefront.Web`, quản trị ở `GaoApp.Web`; dùng chung lớp nghiệp vụ và dữ liệu. Không tự tạo menu.

Yêu cầu tiếp theo ngày 02/11/2026: **“Làm web09 nhưng tạm chưa publish”**. WEB09 đã PASS phần chuẩn bị kỹ thuật sau code/test/review độc lập; không chạy `dotnet publish`, triển khai/mở bán hoặc đổi cấu hình thực tế. Owner UAT, hàng/model thật, thiết bị và môi trường phát hành chưa được coi là đã nghiệm thu. Các bản tài liệu/state được seal WEB08 đã lưu byte chính xác trước khi cập nhật hồ sơ tổng hợp.

## Quy trình bắt buộc mỗi bước

1. Coordinator khóa phạm vi theo plan và đọc trạng thái nguồn hiện tại.
2. Implementer `storefront_code` triển khai production. Riêng WEB07, root nhận UI/controller để làm song song với backend/schema/DI của implementer; cả hai phần production vẫn phải qua test/review độc lập. Tester `storefront_tests` ghi oracle từ yêu cầu trước khi đọc API mới; không sửa production. Reviewer `storefront_review` không sửa code hoặc kỳ vọng test.
3. Khi API ổn định, tester viết test độc lập. Khi code hoàn thành, khóa source/binary bằng hash rồi build, discovery và chạy test trên đúng nguồn.
4. Reviewer kiểm tra độc lập mã, bằng chứng test, quyền/phạm vi và các lỗi có thể gây sai tiền/kho; tự dựng probe khi cần. Không suy PASS từ giao diện hoặc mockup.
5. Có lỗi: ghi nhận nguyên nhân và cách tái hiện, giao implementer sửa. Chạy lại phần bị ảnh hưởng và toàn bộ bộ kiểm tra bắt buộc của bước, reviewer kiểm tra lại bản sửa. Không đổi assertion để che lỗi.
6. Chỉ khi test/review đều đạt, lưu kết quả vào repository, cập nhật plan rồi mới bắt đầu bước kế tiếp.

SQL chỉ dùng fixture GUID có guard và cleanup; không dùng database cửa hàng làm dữ liệu thử. Build/test dùng `.artifacts/storefront-rollout/build`, không dừng tiến trình khác hoặc ghi đè DLL đang mở. Không tự chuyển tiền hoàn, tạo giao dịch ngân hàng thật, đăng ký dịch vụ tính phí hoặc tự đăng hàng thật thay cửa hàng. Các cấu hình thực tế chưa có phải được kiểm tra và giữ cổng mở bán đóng; không ghi đã xác minh hoặc nghiệm thu thật khi chỉ có dữ liệu mô phỏng.

## Theo dõi cổng

| Bước | Code | Test độc lập | Review độc lập | Cổng |
|---|---|---|---|---|
| WEB02 Quản lý hàng đăng | V2 đã hoàn thành | 342/342 đạt | PASS sau sửa P1/P2 | PASS; [báo cáo](STOREFRONT-WEB02-20261031.md) |
| WEB03 Giao diện xem/chọn hàng | V5 đã hoàn thành | 359/359 đạt; browser đủ ba pha | PASS sau sửa R1–R7; identity/cleanup đạt | PASS; [báo cáo](STOREFRONT-WEB03-20261031.md) |
| WEB04 Báo giá/guest COD/giữ hàng | V3 đã hoàn thành | 640 ca distinct đạt, 0 bỏ qua; test V6 | PASS; 84 identity và cleanup đạt | PASS; [báo cáo](STOREFRONT-WEB04-20261031.md) |
| WEB05 QR tự xác nhận | V6 đã hoàn thành | 1.330 ID đạt theo phiên bản; hiện tại 266+498 PASS | PASS; 126 identity và cleanup đạt | PASS; [báo cáo](STOREFRONT-WEB05-20261031.md) |
| WEB06 Soạn/giao/ghi sổ | APP V8 đã hoàn thành | 1.706 ID đạt theo phiên bản; 219+498+157 PASS trên bản cuối WEB06, test V16 | PASS; 192 identity và cleanup đạt | PASS lịch sử; [báo cáo](STOREFRONT-WEB06-20261031.md) |
| WEB07 Hủy và hoàn | APP8 đã hoàn thành | Test12: 1.921 ID hiện tại PASS, 0 bỏ qua; WEB00 riêng 378 kiểm tra PASS | PASS chức năng/source/identity; dọn đúng 1.510 XML được đồng ý và review sau xóa PASS; giữ 1.272 context-only | PASS; [báo cáo](STOREFRONT-WEB07-20261101.md) |
| WEB08 Animation/3D/khả năng truy cập | APP9 hoàn thành; project public riêng | Test5/S9: 1.999 ID hiện tại PASS, 0 bỏ qua; WEB00 riêng 378 assertion PASS | PASS nguồn/DLL, chức năng, chữ200%, chín mẫu lab, tài nguyên và nguồn cuối | PASS phần triển khai; điều kiện phát hành còn mở; [báo cáo](STOREFRONT-WEB08-20261102.md) |
| WEB09 Nghiệm thu/phát hành | APP4/Test7/S6 hoàn tất kỹ thuật; project public riêng | 2.066 ID hiện tại PASS; WEB00 riêng 378 assertion PASS; 0 bỏ qua | PASS nguồn/DLL, năm nhóm, chín mẫu lab và kiểm tra cuối; hồ sơ seal theo gate | PASS phần chuẩn bị trước publish; owner UAT/phát hành thật giữ lại; [báo cáo](STOREFRONT-WEB09-20261102.md) |

Kết quả từng bước sẽ ghi rõ lệnh thực thi, số test đã chạy, source/binary hash, lỗi đã sửa, giới hạn kiểm tra, database/tiến trình fixture trước sau và đường dẫn chức năng cho người dùng tự tạo menu. Không tự ghi hoàn thành bước tiếp theo khi cổng trước chưa đạt.



