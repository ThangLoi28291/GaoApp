# UI-06f — Rà và gom partial ghi chú/giảm giá POS

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu thiết bị thật. Bước tiếp UI-07a: ca hiện tại và mở/nhận/đóng ca.

## Kết quả rà soát và quyết định

Trước bước này, _OrderNoteBox và _OrderDiscountBox là hai template dạng card cũ, không có caller. Cả Prime và legacy đang dùng hai editor viết trực tiếp trong _DraftMetaPanel. Không có hai bản editor được render đồng thời; rủi ro là các template cũ có cùng ID nhưng khác cấu trúc, dễ bị dùng lại sai khi nâng cấp.

Đã thay nội dung hai template cũ bằng đúng hai fragment canonical từ _DraftMetaPanel, rồi để parent gọi mỗi fragment một lần. Prime: Index → _POSPrimeShell → _DraftMetaPanel → hai fragment; legacy: nhánh Index cũ → _DraftMetaPanel → hai fragment. Không thêm wrapper, nút, field hoặc ID. Bung hai partial mới tái tạo HTML cũ ngoài comment và khoảng trắng giữa tag. Giữ nguyên nhãn/class/data hook, textarea, type/min/step và vùng tóm tắt.

PosOrder.bindEvents là nơi gắn lệnh lưu ghi chú/giảm giá/dòng; PosApp có listener đóng/toggle editor. Listener trình bày đó không gửi HTTP hoặc thu tiền, nên giữ nguyên thay vì xóa nhầm. PosPayment, keyboard, Bootstrap và Prime tiếp tục giữ owner cũ. Ctrl+S lưu trực tiếp, không đi qua listener đóng theo button; Ctrl+Enter/Enter theo đường nút gốc. Không thay hành vi desktop để đáp ứng kỳ vọng thử nghiệm khác.

## Phạm vi bảo toàn

Production chỉ đổi 3 Razor partial. Index/layout/header, toàn bộ CSS/JS của snapshot, render/app/order/payment/keyboard/offline/privacy/worker/delivery, controller/service/DTO/quyền/menu/config/migration giữ nguyên. Không thêm API, queue, phép tính, seeder, menu hoặc cấu hình cửa hàng. UI điện thoại vẫn dùng mẫu và adapter đã duyệt; máy tính giữ giao diện hiện tại.

## Kết quả kiểm tra

- Build Release 0 lỗi/cảnh báo. 67/67 hồi quy POS/ID/owner/thu tiền/CSP/offline SQL qua. Contract ID cũ cập nhật để đọc hai child partial khi gom markup, giữ nguyên yêu cầu mỗi ID xuất hiện một lần.
- 8 nhóm browser trên Razor/HTTP/LocalDB thật, không lỗi JS/CSP. Prime, legacy và alias v3 mỗi bản ở 320×568, 360×780, 390×844, 430×932, 600×900, 767×900, 390×440: tổng 21 lượt. Resize giữ chính node input gốc; nhập nháp/chuyển editor không gửi lệnh lưu.
- 5 đối chiếu desktop/tablet: Prime 768/1024/1440, legacy 768/1440. DOM semantic cùng mọi computed property và box của vùng công cụ đơn khớp bản trước refactor. Baseline detached được bổ sung cùng các thuộc tính UI-lock, vùng lỗi và tóm tắt voucher do owner hiện hành thêm lúc chạy; node live gốc được trả lại trong cùng lượt JavaScript.
- 33 lần scan toàn document ở các route/kích thước, sau note/discount/line discount, reload và màn hỏi in: không có ID trùng. Các ID editor/payment canonical đều xuất hiện đúng một lần.
- Lưu ghi chú chậm mạng: nút khóa, bấm lặp không gửi thêm; Ctrl+Enter trim và lưu một lần. Giảm giá âm không gọi API; Enter/save/clear giảm đơn mỗi lần một lệnh. Enter/save/clear giảm dòng dùng modal duy nhất, mỗi lần một lệnh. Legacy Ctrl+S lưu một lần; v3 reload đúng note và giá trị đã lưu.
- Đếm HTTP: 3 note, 3 order-discount, 3 line-discount và 1 cash collection. Bấm Enter lặp lúc đang thu vẫn chỉ một request payment. Dùng lựa chọn Không in gốc theo tùy chọn khách thử.
- SQL: subtotal 131.000, giảm đơn 1.000 + giảm dòng 2.000 = tổng giảm 3.000; GrandTotal/PaidTotal 128.000, còn thiếu/tiền thừa 0. Đúng 1 payment có clientRequestId, 1 đơn Completed; tồn 100 → 99, ca ghi tiền mặt 128.000. Note đúng sau đổi màn hình. Fingerprint menu/store/quầy/giá/tùy chọn khách/store khác không đổi.
- Thiếu quyền giảm giá vẫn bị từ chối bởi server, cửa hàng khác không đọc được đơn hoàn tất.
- Probe tiền gốc không sửa qua: nhập/chèn/xóa/dán/định dạng, VND lẻ, reference thẻ, Enter/QR chờ và sizing. 7 nhóm feedback gốc qua: tự động hóa đơn/busy, reminder, lỗi/thử lại, Không in, popup bị chặn, chống hoàn tất trùng và non-cash.
- 225 file bảo vệ khớp snapshot. Baseline 560 file bất biến: 510 không đổi, 50 đã đổi, 0 mất; ba partial là toàn bộ thay đổi production của bước này. Đã rà ảnh Prime/legacy phone, desktop và giỏ kế tiếp sau chốt.

## Giới hạn và bằng chứng

Chỉ database SQL GUID/Web và Chrome headless/profile riêng. Tắt launcher thường, chặn external traffic và transport in; không chạm database cấu hình hoặc cửa sổ Chrome đang mở. Profile đếm HTTP chặn service worker riêng; production worker/journal giữ nguyên. Không thực hiện in/ngăn kéo/camera/ngân hàng thật.

Còn nghiệm thu Android/iOS thật và các mục thiết bị/người dùng trước đây. UI-06a–f đã có bằng chứng tự động, không đánh dấu các nghiệm thu cũ hoàn tất. Tiếp UI-07a, chưa triển khai màn ca trong bước này.

[Runtime](mobile-ui-20261025/evidence/ui-06f/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-06f/acceptance-matrix.json), [source/topology](mobile-ui-20261025/evidence/ui-06f/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-06f/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-06f-20261026; snapshot/log: .artifacts/ui-06f.
