# UI-08a — Yêu cầu nghiệp vụ trên điện thoại

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu điện thoại thật. Bước tiếp: UI-08b, hàng trả chờ nhập lại.

## Giao diện và bảo toàn

Đường dẫn /admin/pos-shift/requests, tab=cash hoặc tab=payment. Phone dưới 768px có header navy/blue, hai nhóm nghiệp vụ và tab con vừa màn hình, bộ lọc trạng thái/tìm kiếm và danh sách dạng thẻ. Phạm vi ca/chứng từ, số lượng, phân trang, link đối soát và URL cũ giữ nguyên.

Màn tạo yêu cầu và xem/duyệt mở riêng bằng native dialog gốc, toàn màn hình. Tiền đang ghi nhận/trước điều chỉnh/đề nghị và ảnh hưởng quỹ dùng nội dung gốc; trường nhập một cột, từ 16px, vùng chạm từ 44px. Header/footer tách body cuộn theo visualViewport; bàn phím mô phỏng không che nút. Giữ cảnh báo tiền âm, lịch sử/audit, trước/sau, lý do bắt buộc và các nút theo quyền/trạng thái gốc.

Production chỉ đổi Index bằng hai import và thêm hai asset. Hai partial, ba script nghiệp vụ requests/cash/payment, controller/service/DTO, CSS desktop, layout/sidebar/_POSNavTabs, menu/quyền/cấu hình và owner in đều không đổi. Không tạo menu/migration/seeder hoặc API mới.

Adapter chuyển chính action node vào footer bằng marker, không clone field/tiền, tính tiền, fetch/storage hoặc relay click. Submit button gốc liên kết form gốc bằng form= để native validation và listener cũ tiếp tục làm việc. Busy/disabled/Escape do owner cũ. Khi body được vẽ lại, action cũ bị loại trước paint; khi về desktop, vị trí/thuộc tính/role/nhãn phone được hoàn trả.

## Kiểm tra

- Release build 0 lỗi/cảnh báo. 65/65 .NET qua: ca POS, cash/payment/deposit adjustment, unified request page, responsive/CSP/dashboard. 20/20 JS qua; probe requests gốc với API giả vẫn qua tab, URL, phạm vi, form, filter, lazy loading và bàn phím, chỉ GET.
- 5 nhóm browser trên Razor/API/SQL thật. 25 lượt phone gồm 320/360/390/430/600/767px, 390×440, native cash/payment/deposit dialogs và keyboard visualViewport 420px mô phỏng; không tràn ngang, footer trong viewport. 36 scan không ID trùng; không lỗi JS/CSP.
- 10 đối chiếu toàn bộ computed property/box ở 768/1440px cho cả danh sách, form tạo cash/payment và màn admin duyệt cash: desktop giống khi bỏ stylesheet mới, không còn footer/label phone. Input/submit node và giá trị gốc được giữ sau resize.
- Hai danh sách có 24 yêu cầu lịch sử từ chối mỗi loại, phân trang server 20 dòng; chuyển tab giữ bộ lọc/trang riêng và lazy initialization. Không phát sinh lệnh tiền khi chỉ mở/chuyển/lọc danh sách.
- Form thiếu lý do không POST; lỗi 409 giữ draft. Request chậm khóa nút và Escape, double tap chỉ gửi một lần. Tạo yêu cầu chưa đổi số tiền. Native form của cả cash và payment hoạt động sau khi nút chuyển ra footer. Nút cũ bị loại khi tải detail mới; đóng trước response muộn không mở lại dialog.
- SQL setup thực mở ca 200.000, thu thêm 10.000, bán đơn 60 nhận cash 100 + bank 20 và nhận cọc cash 50.000. Chốt/nhận tiền trước test: expected 260.040, actual 259.000, received 258.000. Số dư nhận dư của đơn được giữ nguyên.
- Browser gửi và admin duyệt ba điều chỉnh: thu 10.000 → chi 5.000 (expected 245.040), cash payment 100 → bank (chỉ chuyển bucket cash đã áp 40, expected 245.000), cọc cash 50.000 → bank (expected 195.000). Một payment request khác được rút; một request bị từ chối với lý do bắt buộc. Chỉ 1 cash + 4 payment requests mới, đúng 3 Approved/1 Withdrawn/1 Rejected; đối soát lại xóa cờ của cả ba đã duyệt.
- SQL/fingerprint xác nhận actual 259.000, received 258.000, phiếu chốt expected gốc 260.040/actual 259.000, số tiền payment/ngày nhận, PaidTotal/ChangeDue/BalanceDue, balance cọc, tồn, lịch sử mẫu, menu/store/quầy/giá/tùy chọn khách/bank config/store khác không đổi. Phiếu thu cọc chuyển bank được soft delete đúng owner cũ; không tạo thêm tiền/kho.
- Nhân viên khác không xem yêu cầu của người tạo (403), thiếu quyền tạo hoặc Shift.View bị chặn; store khác không đọc yêu cầu (404). Probe summary dùng đúng phiên thu ngân vì endpoint gốc chỉ cho xem ca của chính mình; kiểm tra policy dùng Accept JSON/maxRedirects=0 để không nhầm redirect HTML access-denied là lệnh thành công. Không sửa server hoặc cấp thêm quyền để làm test qua.
- 362 file bảo vệ khớp snapshot. Baseline 560 file bất biến: 502 không đổi, 58 đã đổi, 0 mất; bước này đổi một view trong baseline và thêm hai asset ngoài baseline. Ảnh danh sách, form, duyệt cash/payment/cọc và trạng thái sau đối soát đã được rà.

## Phạm vi chạy và còn lại

Chỉ GUID LocalDB/Web tạm và Chrome headless/profile mới, launcher thường tắt. Không dùng database cấu hình hoặc đóng/thay đổi cửa sổ Chrome đang mở; chặn thiết bị/transport ngoài. Lịch sử mẫu chỉ phục vụ đọc/phân trang; tất cả thao tác mới chạy qua dịch vụ thật.

Chưa nghiệm thu Android/iOS thật, keyboard/safe-area/một tay, in/két/bank thật. Các nghiệm thu trước, gồm helper két ngoài của UI-07, vẫn mở. UI-08b/c/d chưa được đánh dấu hoàn thành. Chẩn đoán probe trước khi sửa phiên summary/Accept-header giữ ở .artifacts/ui-08a; kết quả cuối và SQL đều qua.

[Runtime](mobile-ui-20261025/evidence/ui-08a/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-08a/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-08a/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-08a/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-08a-20261026; snapshot/log: .artifacts/ui-08a.
