# Sửa 14 test failed ngày 12/09/2026

## Nguồn lỗi và phạm vi

Đọc bản sao log Test Explorer `.vs/GaoApp/v18/TestStore/0/265.testlog` bằng bộ đọc TestLog của Visual Studio. Log có 2.339 kết quả: 2.325 passed, 14 failed. Danh sách lỗi gốc được lưu tại `TestResults/failed-14-fix/failed-baseline.json`; bộ lọc `failed-only.filter` chứa đúng 14 phương thức, mỗi phương thức một test.

Theo yêu cầu của người dùng, chỉ chạy riêng 14 test này; không chạy Run All.

## Thay đổi

- **13 test migration:** cập nhật danh sách kỳ vọng từ 27 lên 29 migration, bổ sung `20260912120000_AddCustomerDisplayWifi` và `20260912150000_AddReceivingPackagingPhoto`. Các kiểm tra migration baseline, nâng cấp không thay đổi dữ liệu cũ, schema/index, model snapshot, chạy lại idempotent và metadata bảo mật vẫn giữ nguyên.
- **1 test kiểm kê quyền:** ghi nhận `ReceiptIntake.Photo` vào danh sách hành động kiểm tra quyền theo tài nguyên. Đã đối chiếu controller và service: kiểm tra quyền view/update/approve theo nguồn phiếu, cửa hàng, kho và ảnh thuộc đúng phiếu. `ReceivingPackagingPhotoHttpTests` hiện có kiểm tra HTTP cho quyền bị từ chối và truy cập khác cửa hàng. Không thay đổi quyền thực tế hay bỏ qua kiểm tra bảo mật.

Chỉ sửa năm file test; không cần migration mới cho lần sửa test này.

## Xác minh

- Build Release thành công, 0 lỗi, 10 cảnh báo có sẵn. Output riêng tại `Logs/pos-regression-20260912-artifacts`; log `Logs/failed-14-build.log`.
- Lần chạy trong sandbox: 2 passed, 12 test SQL gặp `Failed to generate SSPI context`. Đây là lỗi xác thực Windows của môi trường chạy; giữ log `Logs/failed-14-targeted.log` và TRX để đối chiếu.
- Lần xác minh dùng quyền Windows của máy với `.\SQLEXPRESS` và các database test GUID do fixture tạo rồi dọn. Bộ lọc vẫn giữ đúng 14 test gốc. Log: `Logs/failed-14-verified.log`; kết quả: `TestResults/failed-14-fix/failed-14-verified.trx`.
- **Kết quả cuối: 14/14 passed, 0 failed, 0 skipped**, thời gian khoảng 59 giây. Đã so khớp từng tên test trong TRX với 14 lỗi gốc: không thiếu, không thừa. Chi tiết đối chiếu: `TestResults/failed-14-fix/verification.json`.

Đã hoàn tất xác minh nhóm này. Người dùng có thể Build/Rebuild rồi tự chạy Run All trong Visual Studio.
