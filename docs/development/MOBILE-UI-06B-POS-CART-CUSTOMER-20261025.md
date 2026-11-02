# UI-06b — Giỏ hàng, khách hàng và công cụ đơn POS

Ngày 25/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu trên điện thoại thật. Bước tiếp: UI-06c, thanh toán.

## Kết quả

Trên /admin/pos, /admin/pos/v3 và /admin/pos/legacy ở phone <768px:

- Dòng giỏ thành thẻ: tên/quy cách, cụm tăng–giảm/nhập lượng, lượng quy đổi, đơn giá và thành tiền rõ ràng. Giữ nhãn hàng tặng, khuyến mãi, giảm dòng và trạng thái dòng đang chọn. Nút/input tối thiểu 44px, chữ nhập lượng 16px.
- Tác vụ dòng Bootstrap mở trong thẻ để không bị cắt bởi vùng cuộn. Bảng giá giữ đủ đơn vị, hệ số, giá lẻ, giá sỉ và giá áp dụng trong từng thẻ quy cách; không kéo ngang. Sửa một selector của handler Bảng giá cũ để nhận đúng data-price-line-id đã có trên nút.
- Bảng khách hàng/công cụ đơn dùng nền navy/blue: nhận diện khách, điểm/voucher, lịch sử theo quyền; ghi chú và giảm giá tách thành hai mục sửa. Giữ cách đóng editor sau lưu, nội dung thông báo, giá trị và điều kiện nghiệp vụ gốc.
- Popup lượng, giảm dòng, điểm và voucher có nút đóng/nút tác vụ 44px, body cuộn và footer trong giới hạn màn hình. Chạm cả thẻ voucher để chọn. Không tự đổi/tiêu điểm hay hoàn tất đơn.

## Phạm vi bảo toàn

Index chỉ thêm một stylesheet. Không thêm control, ID, handler, API, storage, queue, menu hoặc migration. Không sửa partial/renderer giỏ, customer/rewards, payment, barcode, keyboard, Prime, offline/service worker, camera, controller/service/DTO/quyền hoặc cấu hình cửa hàng. PosOrder chỉ sửa selector Bảng giá; phần còn lại khớp snapshot. CSS mới chỉ áp dụng <768px khi adapter phone hiện có hoạt động. Desktop/tablet giữ CSS/DOM và quyền gốc.

## Kiểm tra và đánh giá

- Build Release cuối: 0 lỗi, 0 cảnh báo; 57/57 hồi quy POS/giá quy cách/tìm/ca/quầy/điểm/voucher/CSP và 3/3 JavaScript qua.
- 14 nhóm browser giỏ trên Razor/HTTP/SQL thật: tăng/giảm/nhập lượng, editor số lượng và quy đổi 2 Lốc = 12 Lon, menu dòng/Bảng giá, giảm dòng 1.000, ghi chú, áp/xóa/áp lại giảm đơn 3.000, xem 200 điểm, chọn voucher 10.000, đổi khách sỉ/lẻ và eligibility, đối chiếu HTTP, resize, legacy/v3, quyền.
- 7 viewport: 320×568, 360×780, 390×844, 430×932, 600×900, 767×900, 390×440. Không tràn ngang; nút lượng/tác vụ 44px; input 16px. Popup điểm/voucher còn kiểm riêng ở 320px. Forced colors/reduced motion có ảnh.
- 5 so sánh toàn bộ computed properties và element boxes với CSS mới bật/tắt: Prime 768/1024/1440, legacy 768/1440; tất cả khớp. Popup bảng giá desktop gốc cũng mở được và đóng bằng nút Đóng ở footer.
- Chạy lại 16 nhóm đầu vào POS gốc ở 10 viewport, gồm Enter/touch/quy cách/scan/prefix/history/quyền/chưa mở ca/fallback và đổi phone/desktop, với service worker thật. Probe gốc không sửa.
- SQL: cùng Draft, bia 2 Lon + 2 Lốc ×6, nước 3 Lon; giảm dòng 1.000, giảm đơn 3.000, voucher áp 10.000, tổng 287.000, đã thu 0. Ghi chú và khách lẻ lưu đúng. Điểm vẫn 200, voucher chưa bị tiêu; không có thanh toán/xuất tồn. Fingerprint menu, tồn, giá quy cách, ca và store khác không đổi.
- 203 file bảo vệ khớp snapshot. Baseline bất biến: 560 file, 514 không đổi, 46 đã đổi, 0 mất. Asset/test mới được ghi riêng trong sourceFiles.

Đã xem ảnh giỏ, quy cách, khách/công cụ, điểm/voucher, phone nhỏ và desktop. Giỏ dễ đọc, tác vụ không bị menu nổi che/cắt; các thao tác vẫn về đúng server và điều kiện gốc. Chỉ dùng SQL GUID/Web thử và Chrome headless/profile riêng; tắt launcher thường, không truy cập database cấu hình hay đóng các cửa sổ Chrome đang mở.

## Bằng chứng và phần còn lại

[Runtime](mobile-ui-20261025/evidence/ui-06b/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-06b/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-06b/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-06b/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-06b-20261025; log/snapshot: .artifacts/ui-06b.

Còn kiểm Android/iOS thật: bàn phím mềm, safe area và thao tác một tay. Camera thật và nghiệm thu các bước trước vẫn còn riêng. Giữ nguyên CSS desktop theo yêu cầu: nút × tại header bảng giá legacy desktop bị clip bởi style cũ; test dùng nút Đóng gốc ở footer, không mở rộng bước phone để sửa style desktop này. UI-06c sẽ xử lý luồng thanh toán điện thoại.
